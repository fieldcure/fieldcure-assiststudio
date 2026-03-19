using System.Diagnostics;
using System.Globalization;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Globalization;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for advanced options such as application language selection.
/// </summary>
public sealed partial class AdvancedPage : Page
{
    #region Fields

    /// <summary>
    /// The language ID that was active when the page was first navigated to,
    /// used to detect changes that require a restart.
    /// </summary>
    private string _initialLanguageId = "";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AdvancedPage"/> class.
    /// </summary>
    public AdvancedPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _initialLanguageId = ApplicationLanguages.PrimaryLanguageOverride ?? "";

        LoadLanguages();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Populates the language picker with the system default option and all manifest-declared languages.
    /// </summary>
    private void LoadLanguages()
    {
        LanguagePicker.Items.Clear();

        // System default option
        LanguagePicker.Items.Add(new LanguageItem("", "System default"));

        // Supported languages from manifest
        foreach (var langTag in ApplicationLanguages.ManifestLanguages)
        {
            try
            {
                var culture = new CultureInfo(langTag);
                LanguagePicker.Items.Add(new LanguageItem(langTag, culture.NativeName));
            }
            catch
            {
                LanguagePicker.Items.Add(new LanguageItem(langTag, langTag));
            }
        }

        // Select current
        var currentId = ApplicationLanguages.PrimaryLanguageOverride ?? "";
        for (var i = 0; i < LanguagePicker.Items.Count; i++)
        {
            if (LanguagePicker.Items[i] is LanguageItem item && item.Id == currentId)
            {
                LanguagePicker.SelectedIndex = i;
                return;
            }
        }

        LanguagePicker.SelectedIndex = 0;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles language picker selection changes, applying the language override
    /// and showing a restart warning if the selection differs from the initial value.
    /// </summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedItem is not LanguageItem selected) return;

        ApplicationLanguages.PrimaryLanguageOverride = selected.Id;

        // Show restart warning if language changed from initial
        LanguageRestartPrompt.Visibility = selected.Id != _initialLanguageId
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Opens the logs folder in File Explorer.
    /// </summary>
    private void OnOpenLogsFolderClick(object sender, RoutedEventArgs e)
    {
        var logsPath = LoggingService.GetLogsFolderPath();
        if (logsPath is not null)
            Process.Start(new ProcessStartInfo(logsPath) { UseShellExecute = true });
    }

    #endregion
}

/// <summary>
/// Represents a selectable language option with an identifier and display name.
/// </summary>
internal sealed class LanguageItem(string id, string displayName)
{
    #region Properties

    /// <summary>
    /// Gets the BCP-47 language tag, or an empty string for the system default.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets the human-readable native name of the language.
    /// </summary>
    public string DisplayName { get; } = displayName;

    #endregion

    #region Overrides

    /// <inheritdoc/>
    public override string ToString() => DisplayName;

    #endregion
}
