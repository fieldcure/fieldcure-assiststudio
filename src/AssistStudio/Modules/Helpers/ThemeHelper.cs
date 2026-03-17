using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace AssistStudio.Modules.Helpers;

/// <summary>
/// Resolves theme-aware brushes from <see cref="Application.Current.Resources"/> ThemeDictionaries.
/// Use this instead of <c>Application.Current.Resources["key"]</c> which does not respond to theme changes.
/// </summary>
public static class ThemeHelper
{
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
