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

    private static AuxiliaryTaskSource ParseSource(string source)
        => source == "Specific" ? AuxiliaryTaskSource.Specific : AuxiliaryTaskSource.Inherit;

    private static string SourceToString(AuxiliaryTaskSource source)
        => source == AuxiliaryTaskSource.Specific ? "Specific" : "Inherit";

    #endregion

    #region Event Handlers

    private void OnTitleSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.TitleSource = SourceToString(TitleSelector.Source);
        AppSettings.TitlePreset = TitleSelector.PresetName;
    }

    private void OnSummarySettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.SummarySource = SourceToString(SummarySelector.Source);
        AppSettings.SummaryPreset = SummarySelector.PresetName;
    }

    private void OnSubAgentSettingsChanged(object? sender, EventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.SubAgentSource = SourceToString(SubAgentSelector.Source);
        AppSettings.SubAgentPreset = SubAgentSelector.PresetName;
    }

    private void OnAutoTitleToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoTitle = AutoTitleToggle.IsOn;
        TitleDetailsPanel.Visibility = AutoTitleToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAutoSummaryToggled(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents) return;
        AppSettings.AppAutoSummary = AutoSummaryToggle.IsOn;
        SummaryDetailsPanel.Visibility = AutoSummaryToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMaxInputTokensChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressEvents || double.IsNaN(args.NewValue)) return;
        AppSettings.AppMaxInputTokens = (int)args.NewValue;
    }

    #endregion
}
