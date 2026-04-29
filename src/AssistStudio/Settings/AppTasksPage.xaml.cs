using AssistStudio.Helpers;
using AssistStudio.Specialists;
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
            AppSettings.TitleModel);

        SummarySelector.Load(
            ParseSource(AppSettings.SummarySource),
            AppSettings.SummaryModel);

        SubAgentSelector.Load(
            ParseSource(AppSettings.SubAgentSource),
            AppSettings.SubAgentModel);

        // Specialists
        LoadSpecialistSelector(WebSearchSpecialistSelector, WebSearchSpecialist.SpecialistName);
        LoadSpecialistSelector(CritiqueSpecialistSelector, CritiqueSpecialist.SpecialistName);
        LoadSpecialistSelector(RedTeamSpecialistSelector, RedTeamSpecialist.SpecialistName);
        LoadSpecialistSelector(DevilsAdvocateSpecialistSelector, DevilsAdvocateSpecialist.SpecialistName);

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
    /// Persists title task source and model when changed.
    /// </summary>
    private void OnTitleSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.TitleSource = SourceToString(TitleSelector.Source);
        AppSettings.TitleModel = TitleSelector.ModelName;
    }

    /// <summary>
    /// Persists summary task source and model when changed.
    /// </summary>
    private void OnSummarySettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.SummarySource = SourceToString(SummarySelector.Source);
        AppSettings.SummaryModel = SummarySelector.ModelName;
    }

    /// <summary>
    /// Persists sub-agent task source and model when changed.
    /// </summary>
    private void OnSubAgentSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.SubAgentSource = SourceToString(SubAgentSelector.Source);
        AppSettings.SubAgentModel = SubAgentSelector.ModelName;
    }

    /// <summary>
    /// Loads source/model for the named specialist into the given selector.
    /// </summary>
    private static void LoadSpecialistSelector(TaskModelSelector selector, string specialistName)
    {
        selector.Load(
            ParseSource(AppSettings.GetSpecialistSource(specialistName)),
            AppSettings.GetSpecialistModel(specialistName));
    }

    /// <summary>
    /// Persists per-specialist source and model when changed.
    /// </summary>
    private void SaveSpecialistSelector(TaskModelSelector selector, string specialistName)
    {
        if (_suppressEvents) return;
        AppSettings.SetSpecialistSource(specialistName, SourceToString(selector.Source));
        AppSettings.SetSpecialistModel(specialistName, selector.ModelName);
    }

    private void OnWebSearchSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(WebSearchSpecialistSelector, WebSearchSpecialist.SpecialistName);

    private void OnCritiqueSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(CritiqueSpecialistSelector, CritiqueSpecialist.SpecialistName);

    private void OnRedTeamSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(RedTeamSpecialistSelector, RedTeamSpecialist.SpecialistName);

    private void OnDevilsAdvocateSpecialistSettingsChanged(object? sender, EventArgs e)
        => SaveSpecialistSelector(DevilsAdvocateSpecialistSelector, DevilsAdvocateSpecialist.SpecialistName);

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
