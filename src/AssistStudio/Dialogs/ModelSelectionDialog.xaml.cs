using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FieldCure.AssistStudio.Helpers;

namespace AssistStudio.Dialogs;

public sealed partial class ModelSelectionDialog : ContentDialog
{
    private readonly OllamaModelManager _manager;
    private readonly HardwareSpec _hw;

    /// <summary>
    /// Models requested to pull. Caller handles the actual download.
    /// </summary>
    public List<string> ModelsToPull { get; } = [];

    public ModelSelectionDialog(OllamaModelManager manager)
    {
        InitializeComponent();
        _manager = manager;
        _hw = HardwareInfo.Detect();

        GpuInfoText.Text = $"GPU: {_hw.GpuName} ({_hw.VramDisplay} VRAM)";
        RamInfoText.Text = $"RAM: {_hw.RamDisplay}";

        Loaded += async (_, _) => await RefreshAsync();
    }

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
            localIds = localModels.Select(m => m.Id).ToHashSet();
        }
        catch
        {
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
        catch
        {
            // Should not happen since SearchAvailableModelsAsync is local
        }
    }

    private void OnDeleteModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            _ = DeleteModelAsync(modelName);
        }
    }

    private async Task DeleteModelAsync(string modelName)
    {
        try
        {
            await _manager.DeleteModelAsync(modelName);
            await RefreshAsync();
        }
        catch
        {
            // Ignore delete errors
        }
    }

    private void OnPullModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            ModelsToPull.Add(modelName);
            btn.IsEnabled = false;
            btn.Content = "Queued";
        }
    }

    private void OnPullCustomModel(object sender, RoutedEventArgs e)
    {
        var name = CustomModelBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            ModelsToPull.Add(name);
            CustomModelBox.Text = "";
        }
    }

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            _ => $"{bytes / 1024.0:F1} KB"
        };
    }
}
