using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace AssistStudio.Helpers;

/// <summary>
/// Centralized theme management service. Persists theme preference via <see cref="AppSettings"/>,
/// applies the theme to the main window, and notifies subscribers of changes.
/// </summary>
public static class ThemeSettingsService
{
    #region Events

    /// <summary>Raised when the application theme changes.</summary>
    public static event EventHandler<ElementTheme>? OnThemeChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the current actual theme based on the main window's root element,
    /// falling back to <see cref="Application.Current.RequestedTheme"/>.
    /// </summary>
    public static ElementTheme ActualTheme
    {
        get
        {
            if (GetMainWindow()?.Content is FrameworkElement root && root.RequestedTheme != ElementTheme.Default)
                return root.RequestedTheme;

            return Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
        }
    }

    /// <summary>
    /// Gets or sets the root theme for the main window, persisting the value to local settings.
    /// </summary>
    public static ElementTheme RootTheme
    {
        get
        {
            if (GetMainWindow()?.Content is FrameworkElement root)
                return root.RequestedTheme;

            return ElementTheme.Default;
        }
        set
        {
            var window = GetMainWindow();
            if (window is not null)
            {
                if (window.Content is FrameworkElement root)
                    root.RequestedTheme = value;

                ApplyTitleBarTheme(window, value);
            }

            var themeName = value switch
            {
                ElementTheme.Light => "Light",
                ElementTheme.Dark => "Dark",
                _ => "System",
            };

            LoggingService.LogInfo($"[{nameof(ThemeSettingsService)}] Theme changed to: {value}");
            AppSettings.SetThemeSilent(themeName);
            OnThemeChanged?.Invoke(null, value);
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Restores the persisted theme without raising <see cref="OnThemeChanged"/>.
    /// Call once at startup after the main window is created.
    /// </summary>
    public static void Initialize()
    {
        var saved = AppSettings.Theme;
        var theme = saved switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        // Apply directly — don't fire OnThemeChanged during startup.
        var window = GetMainWindow();
        if (window is not null)
        {
            if (window.Content is FrameworkElement root)
                root.RequestedTheme = theme;

            ApplyTitleBarTheme(window, theme);
        }

        LoggingService.LogInfo($"[{nameof(ThemeSettingsService)}] Initialized with theme: {saved}");
    }

    /// <summary>
    /// Returns <c>true</c> when the effective theme is dark.
    /// </summary>
    public static bool IsDarkTheme()
    {
        if (RootTheme == ElementTheme.Default)
            return Application.Current.RequestedTheme == ApplicationTheme.Dark;
        return RootTheme == ElementTheme.Dark;
    }

    /// <summary>
    /// Gets the background color appropriate for the current theme.
    /// </summary>
    public static Color GetAppBackgroundColor()
    {
        return IsDarkTheme()
            ? Color.FromArgb(255, 46, 46, 46)
            : Color.FromArgb(255, 240, 240, 240);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Returns the application's main window, or <c>null</c> if unavailable.
    /// </summary>
    private static Window? GetMainWindow()
    {
        return (Application.Current as App)?.MainWindow;
    }

    /// <summary>
    /// Applies theme-appropriate foreground and background colors to the window's title bar buttons.
    /// </summary>
    private static void ApplyTitleBarTheme(Window window, ElementTheme theme)
    {
        if (window.AppWindow?.TitleBar is not { } titleBar)
            return;

        var transparent = Colors.Transparent;
        titleBar.BackgroundColor = transparent;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;

        var isDark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var foreground = isDark ? Colors.White : Colors.Black;
        var hoverBg = isDark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
        var pressedBg = isDark
            ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
        var inactiveFg = isDark
            ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x99, 0x00, 0x00, 0x00);

        titleBar.ForegroundColor = foreground;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBg;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBg;
        titleBar.ButtonInactiveForegroundColor = inactiveFg;
    }

    #endregion
}
