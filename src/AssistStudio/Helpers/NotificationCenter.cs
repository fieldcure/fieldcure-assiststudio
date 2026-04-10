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

    /// <summary>
    /// Posts a persistent notification that can be updated or dismissed by token.
    /// Returns a token used to update or dismiss the notification.
    /// </summary>
    public Guid PostPersistent(InfoBarSeverity severity, string title, string message)
        => _delegate?.PostPersistentNotification(severity, title, message) ?? Guid.Empty;

    /// <summary>
    /// Updates an existing persistent notification.
    /// </summary>
    public void Update(Guid token, string? title = null, string? message = null, InfoBarSeverity? severity = null)
        => _delegate?.UpdateNotification(token, title, message, severity);

    /// <summary>
    /// Dismisses a persistent notification.
    /// </summary>
    public void Dismiss(Guid token)
        => _delegate?.DismissNotification(token);

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

    /// <summary>
    /// Posts a persistent notification that stays until dismissed. Returns a tracking token.
    /// </summary>
    Guid PostPersistentNotification(InfoBarSeverity severity, string title, string message);

    /// <summary>
    /// Updates an existing persistent notification identified by token.
    /// </summary>
    void UpdateNotification(Guid token, string? title, string? message, InfoBarSeverity? severity);

    /// <summary>
    /// Dismisses a persistent notification identified by token.
    /// </summary>
    void DismissNotification(Guid token);
}
