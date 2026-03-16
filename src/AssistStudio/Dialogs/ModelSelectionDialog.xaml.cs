using AssistStudio.Modules.Helpers;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Dialogs;

/// <summary>
/// Dialog for browsing, pulling, and deleting Ollama models.
/// Displays local models and available models with hardware compatibility indicators.
/// </summary>
public sealed partial class ModelSelectionDialog : ContentDialog
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

        try
        {
            var localModels = await _manager.ListLocalModelsAsync();
            var items = localModels.Select(m => new
            {
                m.Id,
                Name = m.Id,
                Size = FormatSize(m.SizeBytes)
            }).ToList();

            LocalModelsList.ItemsSource = items;
            NoLocalModelsText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            localIds = [.. localModels.Select(m => m.Id)];
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            NoLocalModelsText.Visibility = Visibility.Visible;
        }

        try
        {
            var available = await _manager.SearchAvailableModelsAsync();
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
                        CompatIcon = ModelCompatibility.GetCompatibilityIcon(level),
                        CompatText = ModelCompatibility.GetCompatibilityText(level),
                        EstimatedSize = estimated > 0 ? FormatSize(estimated) : "",
                    };
                })
                .ToList();
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
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
    /// Handles the delete button click for a local model.
    /// </summary>
    private void OnDeleteModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            _ = DeleteModelAsync(modelName);
        }
    }

    /// <summary>
    /// Handles the pull button click for an available model, queuing it for download.
    /// </summary>
    private void OnPullModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            ModelsToPull.Add(modelName);
            btn.IsEnabled = false;
            btn.Content = "Queued";
        }
    }

    /// <summary>
    /// Handles pulling a custom model by name entered in the text box.
    /// </summary>
    private void OnPullCustomModel(object sender, RoutedEventArgs e)
    {
        var name = CustomModelBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            ModelsToPull.Add(name);
            CustomModelBox.Text = "";
        }
    }

    #endregion
}
