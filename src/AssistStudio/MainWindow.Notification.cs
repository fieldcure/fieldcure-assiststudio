using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio;

public sealed partial class MainWindow : INotificationDelegate
{
    #region Fields

    private DispatcherTimer? _notificationTimer;

    #endregion

    #region Private Methods

    private void InitializeNotificationCenter()
    {
        NotificationCenter.Instance.SetDelegate(this);

        StatusNotification.Closed += StatusNotification_Closed;

        _notificationTimer = new DispatcherTimer();
        _notificationTimer.Tick += NotificationTimer_Tick;
    }

    #endregion

    #region Event Handlers

    private void NotificationTimer_Tick(object? sender, object e)
    {
        _notificationTimer?.Stop();
        StatusNotification.IsOpen = false;
    }

    private void StatusNotification_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _notificationTimer?.Stop();
    }

    #endregion

    #region INotificationDelegate

    /// <inheritdoc />
    public void PostNotification(InfoBarSeverity severity, string title, string message, int durationMs)
    {
        StatusNotification.Severity = severity;
        StatusNotification.Title = title;
        StatusNotification.Message = message;
        StatusNotification.IsOpen = true;

        if (_notificationTimer is null) return;

        if (_notificationTimer.IsEnabled)
            _notificationTimer.Stop();

        _notificationTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _notificationTimer.Start();
    }

    #endregion
}
