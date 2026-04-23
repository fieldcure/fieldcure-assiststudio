using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AssistStudio.Helpers;

/// <summary>
/// Resolves theme-aware brushes from <see cref="Application.Current.Resources"/> ThemeDictionaries.
/// Use this instead of <c>Application.Current.Resources["key"]</c> which does not respond to theme changes.
/// </summary>
public static class ThemeHelper
{
    /// <summary>
    /// Wires <paramref name="onThemeChanged"/> to <see cref="FrameworkElement.ActualThemeChanged"/>
    /// on <paramref name="host"/> and invokes it once immediately for the initial paint.
    /// <para>
    /// Use this when a control assigns brushes in code via <see cref="GetBrush"/>: the brush
    /// captured at assignment time is a concrete <see cref="SolidColorBrush"/>, not a
    /// <c>{ThemeResource}</c> binding, so it goes stale when the user flips light/dark at runtime.
    /// The callback runs on the host's lifetime — no manual unsubscribe is required as long as
    /// the host itself is collected.
    /// </para>
    /// </summary>
    /// <param name="host">The element whose <see cref="FrameworkElement.ActualTheme"/> drives the resolution.</param>
    /// <param name="onThemeChanged">Re-applies whichever brushes the caller currently needs; invoked once on subscribe and again on every subsequent theme change.</param>
    public static void SubscribeThemeChanges(FrameworkElement host, Action onThemeChanged)
    {
        host.ActualThemeChanged += (_, _) => onThemeChanged();
        onThemeChanged();
    }

    /// <summary>
    /// Resolves a brush from <see cref="Application.Current.Resources"/> ThemeDictionaries
    /// based on the current effective theme.
    /// </summary>
    /// <param name="key">The resource key defined in ThemeDictionaries.</param>
    /// <returns>The theme-appropriate <see cref="Brush"/>.</returns>
    public static Brush GetBrush(string key)
    {
        var themeKey = GetCurrentThemeKey();
        var resources = Application.Current.Resources;

        if (resources.ThemeDictionaries.TryGetValue(themeKey, out var obj) &&
            obj is ResourceDictionary dict && dict.TryGetValue(key, out var value))
        {
            return (Brush)value;
        }

        return (Brush)resources[key];
    }

    /// <summary>
    /// Gets the current theme key ("Light" or "Dark") by inspecting the main window's content root.
    /// </summary>
    private static string GetCurrentThemeKey()
    {
        if (Application.Current is App app)
        {
            // Traverse: App → Window → Content (FrameworkElement) → ActualTheme
            var window = app.MainWindow;
            if (window?.Content is FrameworkElement root)
            {
                return root.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            }
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark ? "Dark" : "Light";
    }
}
