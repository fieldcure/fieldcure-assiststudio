using FluentView.AI.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using FluentView.AI.SampleApp.Helpers;

namespace FluentView.AI.SampleApp.Dialogs;

public sealed partial class ModelSelectionDialog : ContentDialog
{
    private readonly OllamaModelManager _manager;
    private readonly HardwareSpec _hw;

    public string? SelectedModelId { get; private set; }

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
            LocalModelsList.ItemsSource = localModels.Select(m => new
            {
                m.Id,
                Name = m.Id,
                Size = FormatSize(m.SizeBytes)
            }).ToList();

            localIds = localModels.Select(m => m.Id).ToHashSet();
        }
        catch
        {
            // Ollama may not be running — local list stays empty
        }

        // Always show available models (hardcoded list doesn't need Ollama)
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

    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LocalModelsList.SelectedItem is not null)
        {
            dynamic item = LocalModelsList.SelectedItem;
            SelectedModelId = item.Id;
            IsPrimaryButtonEnabled = true;
        }
        else
        {
            IsPrimaryButtonEnabled = false;
        }
    }

    private async void OnDeleteModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
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
    }

    private async void OnPullModel(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string modelName)
        {
            await PullModelAsync(modelName);
        }
    }

    private async void OnPullCustomModel(object sender, RoutedEventArgs e)
    {
        var name = CustomModelBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            await PullModelAsync(name);
        }
    }

    private async Task PullModelAsync(string modelName)
    {
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressStatus.Text = $"Pulling {modelName}...";
        ProgressBar.Value = 0;

        var progress = new Progress<ModelDownloadProgress>(p =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ProgressStatus.Text = p.Status;
                ProgressBar.Value = p.Percent * 100;
            });
        });

        try
        {
            await _manager.DownloadModelAsync(modelName, progress);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            ProgressStatus.Text = $"Error: {ex.Message}";
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
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
