using AssistStudio.Modules.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for configuring utility AI behavior, including the model source,
/// auto-title generation, and auto-summarization toggles.
/// </summary>
public sealed partial class UtilityAIPage : Page
{
    #region Fields

    /// <summary>
    /// Reference to the parent settings panel for accessing presets.
    /// </summary>
    private SettingsPanel? _settings;

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="UtilityAIPage"/> class.
    /// </summary>
    public UtilityAIPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
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

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates the preset combo box with available provider presets and selects the saved choice.
    /// </summary>
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

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles model source radio button changes to switch between "Current" and "Specific" modes.
    /// </summary>
    private void OnModelSourceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (ModelSourceRadio.SelectedItem is RadioButton rb && rb.Tag is string tag)
        {
            AppSettings.UtilityAISource = tag;
            PresetCombo.Visibility = tag == "Specific" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Handles preset combo box selection changes to persist the chosen utility AI preset.
    /// </summary>
    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (PresetCombo.SelectedItem is string name)
        {
            AppSettings.UtilityAIPreset = name;
        }
    }

    /// <summary>
    /// Handles the auto-title toggle switch change to persist the setting.
    /// </summary>
    private void OnAutoTitleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.UtilityAutoTitle = AutoTitleToggle.IsOn;
    }

    /// <summary>
    /// Handles the auto-summary toggle switch change to persist the setting.
    /// </summary>
    private void OnAutoSummaryToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.UtilityAutoSummary = AutoSummaryToggle.IsOn;
    }

    #endregion
}
