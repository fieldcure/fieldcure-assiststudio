using FluentView.AI.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentView.AI.SampleApp;

public sealed partial class ModelSelectionDialog : ContentDialog
{
    private readonly OllamaModelManager _manager;

    public string? SelectedModelId { get; private set; }

    public ModelSelectionDialog(OllamaModelManager manager)
    {
        InitializeComponent();
        _manager = manager;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        try
        {
            var localModels = await _manager.ListLocalModelsAsync();
            LocalModelsList.ItemsSource = localModels.Select(m => new
            {
                m.Id,
                Name = m.Id,
                Size = FormatSize(m.SizeBytes)
            }).ToList();

            var available = await _manager.SearchAvailableModelsAsync();
            var localIds = localModels.Select(m => m.Id).ToHashSet();
            AvailableModelsList.ItemsSource = available
                .Where(m => !localIds.Contains(m.Id))
                .Select(m => new { Name = m.Id, m.DisplayName })
                .ToList();
        }
        catch
        {
            // Ollama may not be running
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
