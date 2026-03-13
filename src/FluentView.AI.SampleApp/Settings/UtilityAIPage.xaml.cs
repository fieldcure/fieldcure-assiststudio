using FluentView.AI.SampleApp.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentView.AI.SampleApp.Settings;

public sealed partial class UtilityAIPage : Page
{
    private SettingsPanel? _settings;
    private bool _suppressEvents;

    public UtilityAIPage()
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

        // Load model source
        var source = AppSettings.UtilityAISource;
        for (var i = 0; i < ModelSourceRadio.Items.Count; i++)
        {
            if (ModelSourceRadio.Items[i] is RadioButton rb && rb.Tag as string == source)
            {
                ModelSourceRadio.SelectedIndex = i;
                break;
            }
        }

        PresetCombo.Visibility = source == "Specific" ? Visibility.Visible : Visibility.Collapsed;

        // Populate presets
        PopulatePresetCombo();

        // Load toggles
        AutoTitleToggle.IsOn = AppSettings.UtilityAutoTitle;
        AutoSummaryToggle.IsOn = AppSettings.UtilityAutoSummary;

        _suppressEvents = false;
    }

    private void PopulatePresetCombo()
    {
        PresetCombo.Items.Clear();
        if (_settings is null) return;

        var selectedName = AppSettings.UtilityAIPreset;
        var selectedIndex = -1;

        for (var i = 0; i < _settings.Presets.Count; i++)
        {
            var preset = _settings.Presets[i];
            PresetCombo.Items.Add(preset.Name);
            if (preset.Name == selectedName)
            {
                selectedIndex = i;
            }
        }

        if (selectedIndex >= 0)
        {
            PresetCombo.SelectedIndex = selectedIndex;
        }
        else if (PresetCombo.Items.Count > 0)
        {
            PresetCombo.SelectedIndex = 0;
        }
    }

    private void OnModelSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (ModelSourceRadio.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            AppSettings.UtilityAISource = tag;
            PresetCombo.Visibility = tag == "Specific" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (PresetCombo.SelectedItem is string name)
        {
            AppSettings.UtilityAIPreset = name;
        }
    }

    private void OnAutoTitleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.UtilityAutoTitle = AutoTitleToggle.IsOn;
    }

    private void OnAutoSummaryToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.UtilityAutoSummary = AutoSummaryToggle.IsOn;
    }
}
