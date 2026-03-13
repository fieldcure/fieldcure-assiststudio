using System.Globalization;
using Windows.Globalization;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

public sealed partial class AdvancedPage : Page
{
    private string _initialLanguageId = "";

    public AdvancedPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _initialLanguageId = ApplicationLanguages.PrimaryLanguageOverride ?? "";

        LoadLanguages();
    }

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

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedItem is not LanguageItem selected) return;

        ApplicationLanguages.PrimaryLanguageOverride = selected.Id;

        // Show restart warning if language changed from initial
        LanguageRestartInfo.IsOpen = selected.Id != _initialLanguageId;
    }
}

internal sealed class LanguageItem(string id, string displayName)
{
    public string Id { get; } = id;
    public string DisplayName { get; } = displayName;

    public override string ToString() => DisplayName;
}
