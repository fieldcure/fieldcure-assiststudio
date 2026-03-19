using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AssistStudio;

public sealed partial class MainWindow : INotificationDelegate
{
    #region Constants

    private const double SlideDistance = 80;
    private static readonly Duration SlideInDuration = new(TimeSpan.FromMilliseconds(300));
    private static readonly Duration SlideOutDuration = new(TimeSpan.FromMilliseconds(250));

    #endregion

    #region Fields

    private DispatcherTimer? _notificationTimer;
    private Storyboard? _slideIn;
    private Storyboard? _slideOut;

    #endregion

    #region Private Methods

    private void InitializeNotificationCenter()
    {
        NotificationCenter.Instance.SetDelegate(this);

        StatusNotification.Closed += StatusNotification_Closed;

        _notificationTimer = new DispatcherTimer();
        _notificationTimer.Tick += NotificationTimer_Tick;

        // Build slide-in storyboard
        _slideIn = CreateSlideStoryboard(SlideDistance, 0, SlideInDuration, new CubicEase { EasingMode = EasingMode.EaseOut });

        // Build slide-out storyboard
        _slideOut = CreateSlideStoryboard(0, SlideDistance, SlideOutDuration, new CubicEase { EasingMode = EasingMode.EaseIn });
        _slideOut.Completed += OnSlideOutCompleted;
    }

    private Storyboard CreateSlideStoryboard(double from, double to, Duration duration, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, NotificationTranslate);
        Storyboard.SetTargetProperty(animation, "Y");

        var sb = new Storyboard();
        sb.Children.Add(animation);
        return sb;
    }

    private void DismissNotification()
    {
        if (_slideOut is null)
        {
            StatusNotification.IsOpen = false;
            return;
        }

        _slideOut.Begin();
    }

    #endregion

    #region Event Handlers

    private void NotificationTimer_Tick(object? sender, object e)
    {
        _notificationTimer?.Stop();
        DismissNotification();
    }

    private void StatusNotification_Closed(InfoBar sender, InfoBarClosedEventArgs args)
    {
        _notificationTimer?.Stop();
    }

    private void OnSlideOutCompleted(object? sender, object e)
    {
        StatusNotification.IsOpen = false;
    }

    #endregion

    #region INotificationDelegate

    /// <inheritdoc />
    public void PostNotification(InfoBarSeverity severity, string title, string message, int durationMs)
    {
        // Stop any in-progress animation/timer
        _slideOut?.Stop();
        _notificationTimer?.Stop();

        StatusNotification.Severity = severity;
        StatusNotification.Title = title;
        StatusNotification.Message = message;
        StatusNotification.IsOpen = true;

        // Slide in from bottom
        _slideIn?.Begin();

        if (_notificationTimer is null) return;

        _notificationTimer.Interval = TimeSpan.FromMilliseconds(durationMs);
        _notificationTimer.Start();
    }

    #endregion
}
