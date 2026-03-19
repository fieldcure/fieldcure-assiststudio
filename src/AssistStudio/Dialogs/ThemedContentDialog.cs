using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Dialogs;

/// <summary>
/// Base class for content dialogs with automatic theme support.
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

    /// <summary>Shared resource loader for localized strings.</summary>
    protected readonly ResourceLoader Loader = new();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemedContentDialog"/> class
    /// with the current application theme applied.
    /// </summary>
    public ThemedContentDialog()
    {
        RequestedTheme = GetCurrentTheme();
        ActualThemeChanged += OnActualThemeChanged;
    }

    #endregion

    #region Private Methods

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        // Re-sync if the app theme changes while the dialog is open
        RequestedTheme = ActualTheme;
    }

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

    #endregion
}
