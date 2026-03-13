using Microsoft.Windows.AppLifecycle;

namespace AssistView.Studio;

public static class Program
{
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
}
