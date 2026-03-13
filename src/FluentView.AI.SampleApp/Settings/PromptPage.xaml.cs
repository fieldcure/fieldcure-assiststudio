using FluentView.AI.Models;
using FluentView.AI.SampleApp.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;

namespace FluentView.AI.SampleApp.Settings;

public sealed partial class PromptPage : Page
{
    private SettingsPanel? _settings;
    private List<PromptPreset> _presets = [];
    private bool _suppressEvents;

    public PromptPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is SettingsPanel settings)
        {
            _settings = settings;
        }

        _suppressEvents = true;
        _presets = AppSettings.LoadPromptPresets();
        PresetListView.ItemsSource = _presets;

        // Select active preset
        var activeName = AppSettings.ActivePromptPreset;
        var activeIndex = _presets.FindIndex(p => p.Name == activeName);
        if (activeIndex >= 0)
        {
            PresetListView.SelectedIndex = activeIndex;
        }
        else if (_presets.Count > 0)
        {
            PresetListView.SelectedIndex = 0;
        }
        _suppressEvents = false;

        LoadSelectedPreset();
    }

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;
        LoadSelectedPreset();
        SaveActivePreset();
    }

    private void LoadSelectedPreset()
    {
        if (PresetListView.SelectedItem is not PromptPreset preset) return;

        _suppressEvents = true;
        PresetNameBox.Text = preset.Name;
        PresetNameBox.IsEnabled = !preset.IsBuiltIn;
        SystemPromptBox.Text = preset.Text;
        _suppressEvents = false;
    }

    private void OnEditorChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (PresetListView.SelectedItem is not PromptPreset preset) return;

        if (!preset.IsBuiltIn)
        {
            preset.Name = PresetNameBox.Text.Trim();
        }
        preset.Text = SystemPromptBox.Text;

        // Refresh list display
        _suppressEvents = true;
        var idx = PresetListView.SelectedIndex;
        PresetListView.ItemsSource = null;
        PresetListView.ItemsSource = _presets;
        PresetListView.SelectedIndex = idx;
        _suppressEvents = false;

        SaveAll();
    }

    private void OnAddPresetClicked(object sender, RoutedEventArgs e)
    {
        var newPreset = new PromptPreset
        {
            Name = "New Preset",
            Text = "",
            IsBuiltIn = false
        };
        _presets.Add(newPreset);

        _suppressEvents = true;
        PresetListView.ItemsSource = null;
        PresetListView.ItemsSource = _presets;
        PresetListView.SelectedIndex = _presets.Count - 1;
        _suppressEvents = false;

        LoadSelectedPreset();
        SaveAll();
        PresetNameBox.Focus(FocusState.Programmatic);
        PresetNameBox.SelectAll();
    }

    private void OnDeletePresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not PromptPreset preset) return;
        if (preset.IsBuiltIn) return;

        var idx = _presets.IndexOf(preset);
        _presets.Remove(preset);

        _suppressEvents = true;
        PresetListView.ItemsSource = null;
        PresetListView.ItemsSource = _presets;
        PresetListView.SelectedIndex = Math.Min(idx, _presets.Count - 1);
        _suppressEvents = false;

        LoadSelectedPreset();
        SaveAll();
    }

    private void SaveAll()
    {
        AppSettings.SaveCustomPromptPresets(_presets);

        // Update current system prompt
        if (PresetListView.SelectedItem is PromptPreset selected)
        {
            AppSettings.ActivePromptPreset = selected.Name;
            _settings?.RaiseSystemPromptChanged(selected.Text);
            _settings?.RaisePromptPresetsChanged();
        }
    }

    private void SaveActivePreset()
    {
        if (PresetListView.SelectedItem is PromptPreset selected)
        {
            AppSettings.ActivePromptPreset = selected.Name;
            AppSettings.SystemPrompt = selected.Text;
            _settings?.RaiseSystemPromptChanged(selected.Text);
        }
    }
}

/// <summary>
/// Converts bool to Visibility: true → Collapsed, false → Visible.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}
