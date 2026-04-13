using Microsoft.UI.Xaml;

namespace AnthropicSdkSample;

/// <summary>
/// Application entry point for the Anthropic SDK sample.
/// </summary>
public partial class App : Application
{
    /// <summary>Initializes the application component.</summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>Creates and activates the main window on launch.</summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }

    /// <summary>The main application window.</summary>
    private Window? _window;
}

/// <summary>
/// WinUI 3 application bootstrap with custom main entry point.
/// </summary>
public static class Program
{
    /// <summary>Application entry point that initializes COM wrappers and starts the WinUI dispatcher.</summary>
    [global::System.STAThreadAttribute]
    static void Main(string[] args)
    {
        global::WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
