using AssistStudio.Helpers;
using FieldCure.AssistStudio.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization.NumberFormatting;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for configuring per-task provider sources (title, summary, sub-agent),
/// auto-title generation, and auto-summarization toggles.
/// </summary>
public sealed partial class AppTasksPage : Page
{
    #region Fields

    private bool _suppressEvents;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AppTasksPage"/> class.
    /// </summary>
    public AppTasksPage()
    {
        InitializeComponent();
        MaxInputTokensBox.NumberFormatter = new DecimalFormatter
        {
            IntegerDigits = 1,
            FractionDigits = 0,
            IsGrouped = true
        };
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _suppressEvents = true;

        // Load per-task selectors
        TitleSelector.Load(
            ParseSource(AppSettings.TitleSource),
            AppSettings.TitlePreset);

        SummarySelector.Load(
            ParseSource(AppSettings.SummarySource),
            AppSettings.SummaryPreset);

        SubAgentSelector.Load(
            ParseSource(AppSettings.SubAgentSource),
            AppSettings.SubAgentPreset);

        // Specialists
        LoadSpecialistSelector(WebSearchSpecialistSelector, "web_search_specialist");
        LoadSpecialistSelector(CritiqueSpecialistSelector, "critique");
        LoadSpecialistSelector(RedTeamSpecialistSelector, "red_team");
        LoadSpecialistSelector(DevilsAdvocateSpecialistSelector, "devils_advocate");

        // Load toggles
        AutoTitleToggle.IsOn = AppSettings.AppAutoTitle;
        AutoSummaryToggle.IsOn = AppSettings.AppAutoSummary;
        MaxInputTokensBox.Value = AppSettings.AppMaxInputTokens;

        // Show details only when toggle is on
        TitleDetailsPanel.Visibility = AutoTitleToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        SummaryDetailsPanel.Visibility = AutoSummaryToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;

        _suppressEvents = false;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Parses a source string into an <see cref="AuxiliaryTaskSource"/> enum value.
    /// </summary>
    private static AuxiliaryTaskSource ParseSource(string source)
        => source == "Specific" ? AuxiliaryTaskSource.Specific : AuxiliaryTaskSource.Inherit;

    /// <summary>
    /// Converts an <see cref="AuxiliaryTaskSource"/> enum value to its string representation.
    /// </summary>
    private static string SourceToString(AuxiliaryTaskSource source)
        => source == AuxiliaryTaskSource.Specific ? "Specific" : "Inherit";

    #endregion

    #region Event Handlers

    /// <summary>
    /// Persists title task source and preset when changed.
    /// </summary>
    private void OnTitleSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.TitleSource = SourceToString(TitleSelector.Source);
        AppSettings.TitlePreset = TitleSelector.PresetName;
    }

    /// <summary>
    /// Persists summary task source and preset when changed.
    /// </summary>
    private void OnSummarySettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.SummarySource = SourceToString(SummarySelector.Source);
        AppSettings.SummaryPreset = SummarySelector.PresetName;
    }

    /// <summary>
    /// Persists sub-agent task source and preset when changed.
    /// </summary>
    private void OnSubAgentSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.SubAgentSource = SourceToString(SubAgentSelector.Source);
        AppSettings.SubAgentPreset = SubAgentSelector.PresetName;
    }

    /// <summary>
    /// Loads source/preset for the named specialist into the given selector.
    /// </summary>
    private static void LoadSpecialistSelector(TaskPresetSelector selector, string specialistName)
    {
        selector.Load(
            ParseSource(AppSettings.GetSpecialistSource(specialistName)),
            AppSettings.GetSpecialistPreset(specialistName));
    }

    /// <summary>
    /// Persists per-specialist source and preset when changed.
    /// </summary>
    private void SaveSpecialistSelector(TaskPresetSelector selector, string specialistName)
    {
        if (_suppressEvents) return;
        AppSettings.SetSpecialistSource(specialistName, SourceToString(selector.Source));
        AppSettings.SetSpecialistPreset(specialistName, selector.PresetName);
    }

    private void OnWebSearchSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(WebSearchSpecialistSelector, "web_search_specialist");

    private void OnCritiqueSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(CritiqueSpecialistSelector, "critique");

    private void OnRedTeamSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(RedTeamSpecialistSelector, "red_team");

    private void OnDevilsAdvocateSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(DevilsAdvocateSpecialistSelector, "devils_advocate");

    /// <summary>
    /// Handles the auto-title toggle and updates panel visibility.
    /// </summary>
    private void OnAutoTitleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoTitle = AutoTitleToggle.IsOn;
        TitleDetailsPanel.Visibility = AutoTitleToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles the auto-summary toggle and updates panel visibility.
    /// </summary>
    private void OnAutoSummaryToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoSummary = AutoSummaryToggle.IsOn;
        SummaryDetailsPanel.Visibility = AutoSummaryToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Persists the max input tokens value when changed.
    /// </summary>
    private void OnMaxInputTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || double.IsNaN(args.NewValue)) return;
        AppSettings.AppMaxInputTokens = (int)args.NewValue;
    }

    #endregion
}
