using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.Helpers;

/// <summary>
/// Centralized notification system using the singleton + delegate pattern.
/// Register a delegate (typically the main window) to display notifications.
/// Safe to call from any thread — all delegate invocations are marshalled to the
/// UI thread captured at <see cref="SetDelegate"/> time.
/// </summary>
public sealed class NotificationCenter
{
    #region Fields

    private static NotificationCenter? _instance;
    private INotificationDelegate? _delegate;
    private DispatcherQueue? _dispatcherQueue;

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
    /// Sets the delegate responsible for displaying notifications. Must be called
    /// from the UI thread — the current thread's <see cref="DispatcherQueue"/> is
    /// captured and used to marshal subsequent calls from any thread.
    /// </summary>
    public void SetDelegate(INotificationDelegate notificationDelegate)
    {
        _delegate = notificationDelegate;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    /// <summary>
    /// Posts a notification with the specified severity, title, message, and auto-dismiss duration.
    /// </summary>
    public void Post(InfoBarSeverity severity, string title, string message, int durationMs = 3000)
        => Invoke(d => d.PostNotification(severity, title, message, durationMs));

    /// <summary>
    /// Posts an informational notification.
    /// </summary>
    public void Post(string title, string message = "", int durationMs = 3000)
        => Invoke(d => d.PostNotification(InfoBarSeverity.Informational, title, message, durationMs));

    /// <summary>
    /// Posts a persistent notification that can be updated or dismissed by token.
    /// Returns a token used to update or dismiss the notification.
    /// Blocks on the UI dispatcher if called from a background thread, because the
    /// caller needs the token value synchronously.
    /// </summary>
    public Guid PostPersistent(InfoBarSeverity severity, string title, string message)
    {
        var target = _delegate;
        if (target is null) return Guid.Empty;

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            return target.PostPersistentNotification(severity, title, message);

        var tcs = new TaskCompletionSource<Guid>();
        _dispatcherQueue.TryEnqueue(() =>
        {
            try { tcs.SetResult(target.PostPersistentNotification(severity, title, message)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Updates an existing persistent notification.
    /// </summary>
    public void Update(Guid token, string? title = null, string? message = null, InfoBarSeverity? severity = null)
        => Invoke(d => d.UpdateNotification(token, title, message, severity));

    /// <summary>
    /// Dismisses a persistent notification.
    /// </summary>
    public void Dismiss(Guid token)
        => Invoke(d => d.DismissNotification(token));

    /// <summary>
    /// Invokes a delegate action on the captured UI dispatcher, or synchronously if
    /// already on the UI thread. No-op when no delegate is registered.
    /// </summary>
    private void Invoke(Action<INotificationDelegate> action)
    {
        var target = _delegate;
        if (target is null) return;

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
            action(target);
        else
            _dispatcherQueue.TryEnqueue(() => action(target));
    }

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
