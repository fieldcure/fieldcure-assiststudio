using FluentView.AI.Providers;
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
        try
        {
            using var client = new HttpClient();
            var response = await client.GetAsync("http://localhost:11434/api/tags");
            OllamaStatusText.Text = response.IsSuccessStatusCode
                ? "Status: \u2705 Running"
                : $"Status: \u274C Error ({response.StatusCode})";
        }
        catch
        {
            OllamaStatusText.Text = "Status: \u274C Not running";
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
