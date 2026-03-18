using AssistStudio.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for visual personalization options such as application theme selection.
/// </summary>
public sealed partial class PersonalizationPage : Page
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="PersonalizationPage"/> class.
    /// </summary>
    public PersonalizationPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Load current theme
        var theme = AppSettings.Theme;
        for (var i = 0; i < ThemeRadioButtons.Items.Count; i++)
        {
            if (ThemeRadioButtons.Items[i] is RadioButton rb && rb.Tag as string == theme)
            {
                ThemeRadioButtons.SelectedIndex = i;
                break;
            }
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles theme radio button selection changes.
    /// </summary>
    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeRadioButtons.SelectedItem is RadioButton rb && rb.Tag is string theme)
        {
            AppSettings.Theme = theme;
        }
    }

    #endregion
}
