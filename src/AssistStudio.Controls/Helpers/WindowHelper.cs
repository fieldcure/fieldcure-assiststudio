using Microsoft.UI.Xaml;

namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Helper to track active windows for FileOpenPicker initialization.
/// Consumers must call <see cref="TrackWindow"/> in their App.OnLaunched.
/// </summary>
public static class WindowHelper
{
    #region Fields

    /// <summary>
    /// The list of currently active windows registered via <see cref="TrackWindow"/>.
    /// </summary>
    internal static readonly List<Window> ActiveWindows = [];

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers a window so controls can find it for native interop (e.g. FileOpenPicker).
    /// Call this once per window in your App.OnLaunched or Window constructor.
    /// </summary>
    public static void TrackWindow(Window window)
    {
        if (!ActiveWindows.Contains(window))
        {
            ActiveWindows.Add(window);
            window.Closed += (_, _) => ActiveWindows.Remove(window);
        }
    }

    #endregion
}
