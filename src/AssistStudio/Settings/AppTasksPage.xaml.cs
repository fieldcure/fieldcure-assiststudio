using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for configuring app tasks behavior: model source selection,
/// auto-title generation, and auto-summarization toggles.
/// Embedding and contextualizer settings have moved to the Connect page (Knowledge Archive card).
/// </summary>
public sealed partial class AppTasksPage : Page
{
    #region Fields

    /// <summary>
    /// Flag to suppress event handlers during programmatic UI updates.
    /// </summary>
    private bool _suppressEvents;

    private readonly ResourceLoader _loader = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AppTasksPage"/> class.
    /// </summary>
    public AppTasksPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _suppressEvents = true;

        // Load model source
        var source = AppSettings.AppTasksSource;
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
        AutoTitleToggle.IsOn = AppSettings.AppAutoTitle;
        AutoSummaryToggle.IsOn = AppSettings.AppAutoSummary;
        MaxInputTokensBox.Value = AppSettings.AppMaxInputTokens;
        SummaryThresholdPanel.Visibility = AutoSummaryToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

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

        var items = AppSettings.BuildOrderedPresetItems();
        var selectedName = AppSettings.AppTasksPreset;
        var selectedIndex = -1;

        foreach (var obj in items)
        {
            if (obj is ProviderPreset preset)
            {
                var displayName = preset.ProviderType == "Mock" ? "Demo" : preset.Name;
                PresetCombo.Items.Add(displayName);
                if (preset.Name == selectedName)
                {
                    selectedIndex = PresetCombo.Items.Count - 1;
                }
            }
            else if (obj is "-")
            {
                var border = (Border)XamlReader.Load(
                    """
                    <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                            Height="1" HorizontalAlignment="Stretch"
                            Background="{ThemeResource DividerStrokeColorDefaultBrush}" />
                    """);
                PresetCombo.Items.Add(new ComboBoxItem
                {
                    IsEnabled = false,
                    IsHitTestVisible = false,
                    MinHeight = 0,
                    Height = 9,
                    Padding = new Thickness(0),
                    Content = border,
                });
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
            AppSettings.AppTasksSource = tag;
            PresetCombo.Visibility = tag == "Specific" ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Handles preset combo box selection changes to persist the chosen app tasks preset.
    /// </summary>
    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents) return;

        if (PresetCombo.SelectedItem is string name)
        {
            AppSettings.AppTasksPreset = name;
        }
    }

    /// <summary>
    /// Handles the auto-title toggle switch change to persist the setting.
    /// </summary>
    private void OnAutoTitleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoTitle = AutoTitleToggle.IsOn;
    }

    /// <summary>
    /// Handles the auto-summary toggle switch change to persist the setting.
    /// </summary>
    private void OnAutoSummaryToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoSummary = AutoSummaryToggle.IsOn;
        SummaryThresholdPanel.Visibility = AutoSummaryToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the max input tokens number box value change to persist the setting.
    /// </summary>
    private void OnMaxInputTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || double.IsNaN(args.NewValue)) return;
        AppSettings.AppMaxInputTokens = (int)args.NewValue;
    }

    #endregion
}
