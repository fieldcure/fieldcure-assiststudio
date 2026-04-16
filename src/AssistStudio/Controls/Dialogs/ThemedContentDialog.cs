using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using Windows.UI;

namespace AssistStudio.Controls.Dialogs;

/// <summary>
/// Base class for content dialogs with automatic theme support and consistent styling.
/// Resolves the current theme from the app's root element and applies it,
/// eliminating the need to set <c>RequestedTheme</c> on every dialog instance.
/// </summary>
/// <remarks>
/// Callers must still set <see cref="ContentDialog.XamlRoot"/> before calling
/// <see cref="ContentDialog.ShowAsync"/>.
/// </remarks>
public partial class ThemedContentDialog : ContentDialog
{
    #region Fields

    private static readonly SolidColorBrush DarkBackground = new(Color.FromArgb(255, 0x10, 0x10, 0x10));
    private static readonly SolidColorBrush LightBackground = new(Colors.White);

    /// <summary>Shared resource loader for localized strings.</summary>
    protected readonly ResourceLoader Loader = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedContentDialog"/> class
    /// with the current application theme and background applied.
    /// </summary>
    public ThemedContentDialog()
    {
        RequestedTheme = GetCurrentTheme();
        Background = IsDarkTheme() ? DarkBackground : LightBackground;
        ActualThemeChanged += OnActualThemeChanged;
    }

    #endregion

    #region Static Helpers

    /// <summary>
    /// Creates a button style with the specified background color and white foreground.
    /// </summary>
    internal static Style GetButtonStyle(Color backgroundColor)
    {
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(BackgroundProperty, backgroundColor));
        style.Setters.Add(new Setter(ForegroundProperty, Colors.White));
        return style;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Updates the dialog background when the theme changes while the dialog is open.
    /// </summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        Background = ActualTheme == ElementTheme.Dark ? DarkBackground : LightBackground;
    }

    /// <summary>
    /// Resolves the current effective theme from the main window's root element.
    /// </summary>
    private static ElementTheme GetCurrentTheme()
    {
        if (Application.Current is App app &&
            app.MainWindow?.Content is FrameworkElement root)
        {
            return root.ActualTheme;
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;
    }

    /// <summary>
    /// Returns <c>true</c> when the effective theme is dark.
    /// </summary>
    private static bool IsDarkTheme()
    {
        var theme = GetCurrentTheme();
        if (theme == ElementTheme.Default)
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return theme == ElementTheme.Dark;
    }

    #endregion
}
