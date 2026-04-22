using Microsoft.Windows.AppLifecycle;

namespace AssistStudio;

/// <summary>
/// Application bootstrap that ensures single-instance behavior and initializes the WinUI application.
/// </summary>
public static class Program
{
    #region Entry Point

    /// <summary>
    /// Main entry point for the application. Checks for single-instance redirection before starting.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        var isRedirect = DecideRedirection();
        if (!isRedirect)
        {
            Microsoft.UI.Xaml.Application.Start(p =>
            {
                var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Determines whether this instance should redirect its activation to the already-running main instance.
    /// </summary>
    /// <returns><c>true</c> if activation was redirected to another instance; <c>false</c> if this is the main instance.</returns>
    private static bool DecideRedirection()
    {
        var mainInstance = AppInstance.FindOrRegisterForKey("main");
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();

        if (!mainInstance.IsCurrent)
        {
            mainInstance.RedirectActivationToAsync(activatedArgs).AsTask().GetAwaiter().GetResult();
            return true;
        }

        return false;
    }

    #endregion
}
