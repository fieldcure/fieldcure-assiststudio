using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FluentView.AI.SampleApp.Settings;

public sealed partial class PersonalizationPage : Page
{
    private SettingsPanel? _settings;

    public PersonalizationPage()
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

        // Load current theme
        var theme = AppSettings.Theme;
        for (int i = 0; i < ThemeRadioButtons.Items.Count; i++)
        {
            if (ThemeRadioButtons.Items[i] is RadioButton rb && rb.Tag as string == theme)
            {
                ThemeRadioButtons.SelectedIndex = i;
                break;
            }
        }
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeRadioButtons.SelectedItem is RadioButton rb && rb.Tag is string theme)
        {
            _settings?.RaiseThemeChanged(theme);
        }
    }
}
