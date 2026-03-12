using FluentView.AI.Providers;
using FluentView.AI.SampleApp.Dialogs;
using FluentView.AI.SampleApp.Helpers;
using FluentView.AI.SampleApp.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentView.AI.SampleApp.Settings;

public sealed partial class ModelsPage : Page
{
    private SettingsPanel? _settings;

    public ModelsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SettingsPanel settings)
        {
            _settings = settings;
            PresetListView.ItemsSource = _settings.Presets;
        }
    }

    // ===== Preset Management =====

    private void OnPresetProviderTypeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo && combo.Tag is ProviderPreset preset)
        {
            combo.Items.Clear();
            foreach (var type in ProviderFactory.ProviderTypes)
            {
                combo.Items.Add(type);
            }

            var index = Array.IndexOf(ProviderFactory.ProviderTypes, preset.ProviderType);
            combo.SelectedIndex = index >= 0 ? index : 0;
        }
    }

    private void OnPresetProviderTypeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.Tag is ProviderPreset preset &&
            combo.SelectedItem is string selected)
        {
            preset.ProviderType = selected;
        }
    }

    private void OnAddPreset(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;

        _settings.Presets.Add(new ProviderPreset
        {
            Name = $"Preset {_settings.Presets.Count + 1}",
            ProviderType = "Mock"
        });
    }

    private void OnDeletePreset(object sender, RoutedEventArgs e)
    {
        if (_settings is null) return;

        if (sender is Button btn && btn.Tag is ProviderPreset preset)
        {
            PasswordVaultHelper.DeleteApiKey(preset.Name);
            _settings.Presets.Remove(preset);
        }
    }

    private void OnSavePresets(object sender, RoutedEventArgs e)
    {
        _settings?.RaisePresetsChanged();
    }

    // ===== Ollama =====

    private async void OnCheckOllama(object sender, RoutedEventArgs e)
    {
        await CheckOllamaStatusAsync();
    }

    private async Task CheckOllamaStatusAsync()
    {
        OllamaSpinner.Visibility = Visibility.Visible;
        OllamaSpinner.IsActive = true;
        OllamaStatusText.Text = "Status: checking...";
        StartOllamaButton.Visibility = Visibility.Collapsed;
        InstallPanel.Visibility = Visibility.Collapsed;

        try
        {
            var isRunning = await OllamaHelper.IsOllamaRunningAsync();
            if (isRunning)
            {
                OllamaStatusText.Text = "Status: \u2705 Running";
            }
            else if (OllamaHelper.IsOllamaInstalled())
            {
                OllamaStatusText.Text = "Status: \u26A0\uFE0F Installed but not running";
                StartOllamaButton.Visibility = Visibility.Visible;
            }
            else
            {
                OllamaStatusText.Text = "Status: \u274C Not installed";
                InstallPanel.Visibility = Visibility.Visible;
            }
        }
        catch
        {
            OllamaStatusText.Text = "Status: \u274C Error";
        }
        finally
        {
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnStartOllama(object sender, RoutedEventArgs e)
    {
        StartOllamaButton.IsEnabled = false;
        OllamaSpinner.Visibility = Visibility.Visible;
        OllamaSpinner.IsActive = true;
        OllamaStatusText.Text = "Status: Starting Ollama...";

        try
        {
            var started = await OllamaHelper.StartOllamaAsync();
            if (started)
            {
                OllamaStatusText.Text = "Status: \u2705 Running";
                StartOllamaButton.Visibility = Visibility.Collapsed;
            }
            else
            {
                OllamaStatusText.Text = "Status: \u274C Failed to start";
            }
        }
        catch
        {
            OllamaStatusText.Text = "Status: \u274C Error starting Ollama";
        }
        finally
        {
            StartOllamaButton.IsEnabled = true;
            OllamaSpinner.IsActive = false;
            OllamaSpinner.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnBrowseOllamaModels(object sender, RoutedEventArgs e)
    {
        using var manager = new OllamaModelManager();
        var dialog = new ModelSelectionDialog(manager)
        {
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
