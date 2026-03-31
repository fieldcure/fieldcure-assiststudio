using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AssistStudio.Dialogs;

/// <summary>
/// Dialog for browsing, pulling, and deleting Ollama models.
/// Displays local models and available models with hardware compatibility indicators.
/// </summary>
public sealed partial class ModelSelectionDialog : ThemedContentDialog
{
    #region Fields

    /// <summary>
    /// The Ollama model manager used to list, pull, and delete models.
    /// </summary>
    private readonly OllamaModelManager _manager;

    /// <summary>
    /// Detected hardware specifications for compatibility checking.
    /// </summary>
    private readonly HardwareSpec _hw;

    /// <summary>
    /// The button that triggered the VRAM warning, pending user confirmation.
    /// </summary>
    private Button? _pendingPullButton;

    #endregion

    #region Properties

    /// <summary>
    /// Models requested to pull. Caller handles the actual download.
    /// </summary>
    public List<string> ModelsToPull { get; } = [];

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelSelectionDialog"/> class with
    /// the given model manager and detects hardware for compatibility display.
    /// </summary>
    public ModelSelectionDialog(OllamaModelManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _hw = HardwareInfo.Detect();

        GpuInfoText.Text = $"GPU: {_hw.GpuName} ({_hw.VramDisplay} VRAM)";
        RamInfoText.Text = $"RAM: {_hw.RamDisplay}";

        Loaded += async (_, _) => await RefreshAsync();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Refreshes both the local and available model lists, filtering out already-installed models.
    /// </summary>
    private async Task RefreshAsync()
    {
        var localIds = new HashSet<string>();

        // Show shimmer placeholders while loading
        LocalModelsShimmer.Visibility = Visibility.Visible;
        LocalModelsList.Visibility = Visibility.Collapsed;
        NoLocalModelsPanel.Visibility = Visibility.Collapsed;
        AvailableModelsShimmer.Visibility = Visibility.Visible;
        AvailableModelsList.Visibility = Visibility.Collapsed;

        try
        {
            var localModels = await _manager.ListLocalModelsAsync();
            var deleteTooltip = Loader.GetString("ModelDialog_DeleteTooltip");
            var items = localModels.Select(m => new
            {
                m.Id,
                Name = m.Id,
                Size = FormatSize(m.SizeBytes),
                DeleteTooltip = deleteTooltip,
            }).ToList();

            LocalModelsList.ItemsSource = items;
            LocalModelsShimmer.Visibility = Visibility.Collapsed;
            LocalModelsList.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoLocalModelsPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            // Include both full name (e.g. "llava:latest") and base name ("llava")
            // so Available list correctly filters out already-downloaded models.
            foreach (var m in localModels)
            {
                localIds.Add(m.Id);
                var colonIdx = m.Id.IndexOf(':');
                if (colonIdx > 0)
                    localIds.Add(m.Id[..colonIdx]);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            LocalModelsShimmer.Visibility = Visibility.Collapsed;
            NoLocalModelsPanel.Visibility = Visibility.Visible;
        }

        try
        {
            var available = await _manager.SearchAvailableModelsAsync();
            var downloadTooltip = Loader.GetString("ModelDialog_DownloadTooltip");
            AvailableModelsList.ItemsSource = available
                .Where(m => !localIds.Contains(m.Id))
                .Select(m =>
                {
                    var estimated = ModelCompatibility.GetEstimatedSize(m.Id);
                    var level = estimated > 0
                        ? ModelCompatibility.Check(estimated, _hw)
                        : CompatibilityLevel.Unknown;
                    return new
                    {
                        Name = m.Id,
                        m.DisplayName,
                        CompatLevel = level,
                        CompatGlyph = GetCompatGlyph(level),
                        CompatBrush = GetCompatBrush(level),
                        CompatText = ModelCompatibility.GetCompatibilityText(level),
                        EstimatedSize = estimated > 0 ? FormatSize(estimated) : "",
                        DownloadTooltip = downloadTooltip,
                    };
                })
                .ToList();

            AvailableModelsShimmer.Visibility = Visibility.Collapsed;
            AvailableModelsList.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            AvailableModelsShimmer.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Returns a Segoe Fluent Icons glyph for the given compatibility level.
    /// </summary>
    private static string GetCompatGlyph(CompatibilityLevel level) => level switch
    {
        CompatibilityLevel.Compatible => "\uE73E",      // CheckMark
        CompatibilityLevel.NotRecommended => "\uE7BA",   // Warning
        CompatibilityLevel.NotCompatible => "\uE711",    // Cancel
        _ => "\uE9CE"                                    // Unknown
    };

    /// <summary>
    /// Returns a themed brush for the given compatibility level.
    /// </summary>
    private Brush GetCompatBrush(CompatibilityLevel level)
    {
        var key = level switch
        {
            CompatibilityLevel.Compatible => "SystemFillColorSuccessBrush",
            CompatibilityLevel.NotRecommended => "SystemFillColorCautionBrush",
            CompatibilityLevel.NotCompatible => "SystemFillColorCriticalBrush",
            _ => "SystemFillColorNeutralBrush"
        };

        if (Resources.TryGetValue(key, out var resource) && resource is Brush brush)
            return brush;

        // Fallback: try application-level resources
        if (Application.Current.Resources.TryGetValue(key, out resource) && resource is Brush appBrush)
            return appBrush;

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    /// <summary>
    /// Deletes the specified model from the local Ollama instance and refreshes the lists.
    /// </summary>
    private async Task DeleteModelAsync(string modelName)
    {
        try
        {
            await _manager.DeleteModelAsync(modelName);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    /// <summary>
    /// Shows a confirmation dialog before deleting the specified model.
    /// </summary>
    /// <returns>True if the user confirmed deletion; otherwise, false.</returns>
    private async Task<bool> ConfirmDeleteAsync(string modelName)
    {
        var dialog = new ThemedContentDialog
        {
            Title = Loader.GetString("ModelDialog_DeleteConfirmTitle"),
            Content = string.Format(Loader.GetString("ModelDialog_DeleteConfirmMessage"), modelName),
            PrimaryButtonText = Loader.GetString("ModelDialog_Delete/Content"),
            CloseButtonText = Loader.GetString("ModelDialog_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Formats a byte count into a human-readable size string (KB, MB, or GB).
    /// </summary>
    /// <returns>A formatted size string such as "4.2 GB".</returns>
    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            _ => $"{bytes / 1024.0:F1} KB"
        };
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the delete button click for a local model with confirmation.
    /// </summary>
    private async void OnDeleteModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            if (await ConfirmDeleteAsync(modelName))
            {
                await DeleteModelAsync(modelName);
            }
        }
    }

    /// <summary>
    /// Handles the pull button click for an available model, queuing it for download.
    /// Shows a VRAM warning TeachingTip if the model is not compatible.
    /// </summary>
    private void OnPullModel(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string modelName)
            return;

        // Check compatibility from the bound data
        if (btn.DataContext is { } dc)
        {
            var level = (CompatibilityLevel)dc.GetType().GetProperty("CompatLevel")!.GetValue(dc)!;
            if (level == CompatibilityLevel.NotCompatible)
            {
                _pendingPullButton = btn;
                VramWarningTip.Target = btn;
                VramWarningTip.IsOpen = true;
                return;
            }
        }

        QueuePull(btn, modelName);
    }

    /// <summary>
    /// Confirms the VRAM warning and proceeds with download.
    /// </summary>
    private void OnVramWarningConfirm(TeachingTip sender, object args)
    {
        VramWarningTip.IsOpen = false;
        if (_pendingPullButton is { Tag: string modelName } btn)
        {
            QueuePull(btn, modelName);
        }
        _pendingPullButton = null;
    }

    /// <summary>
    /// Cancels the VRAM warning.
    /// </summary>
    private void OnVramWarningCancel(TeachingTip sender, object args)
    {
        VramWarningTip.IsOpen = false;
        _pendingPullButton = null;
    }

    /// <summary>
    /// Queues a model for download and updates the button state.
    /// </summary>
    private void QueuePull(Button btn, string modelName)
    {
        ModelsToPull.Add(modelName);
        btn.IsEnabled = false;
        btn.Content = new FontIcon { Glyph = "\uE73E", FontSize = 14 };
    }

    /// <summary>
    /// Handles pulling a custom model by name entered in the text box.
    /// </summary>
    private void OnPullCustomModel(object sender, RoutedEventArgs e)
    {
        var name = CustomModelBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name) && !ModelsToPull.Contains(name))
        {
            ModelsToPull.Add(name);
            CustomModelBox.Text = "";
        }
    }

    #endregion
}
