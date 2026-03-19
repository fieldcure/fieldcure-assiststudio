using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Helpers;

/// <summary>
/// Centralized notification system using the singleton + delegate pattern.
/// Register a delegate (typically the main window) to display notifications.
/// </summary>
public sealed class NotificationCenter
{
    #region Fields

    private static NotificationCenter? _instance;
    private INotificationDelegate? _delegate;

    #endregion

    #region Constructor

    private NotificationCenter() { }

    #endregion

    #region Properties

    /// <summary>Gets the singleton instance.</summary>
    public static NotificationCenter Instance => _instance ??= new NotificationCenter();

    #endregion

    #region Methods

    /// <summary>
    /// Sets the delegate responsible for displaying notifications.
    /// </summary>
    public void SetDelegate(INotificationDelegate notificationDelegate)
        => _delegate = notificationDelegate;

    /// <summary>
    /// Posts a notification with the specified severity, title, message, and auto-dismiss duration.
    /// </summary>
    public void Post(InfoBarSeverity severity, string title, string message, int durationMs = 3000)
        => _delegate?.PostNotification(severity, title, message, durationMs);

    /// <summary>
    /// Posts an informational notification.
    /// </summary>
    public void Post(string title, string message = "", int durationMs = 3000)
        => _delegate?.PostNotification(InfoBarSeverity.Informational, title, message, durationMs);

    #endregion
}

/// <summary>
/// Interface for displaying notifications.
/// </summary>
public interface INotificationDelegate
{
    /// <summary>
    /// Displays a notification with the specified parameters.
    /// </summary>
    void PostNotification(InfoBarSeverity severity, string title, string message, int durationMs);
}
