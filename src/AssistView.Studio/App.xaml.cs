using FluentView.AI.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace AssistView.Studio;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        ConversationManager.Initialize(ApplicationData.Current.LocalFolder.Path);

        _window = new MainWindow();
        FluentView.AI.Controls.WindowHelper.TrackWindow(_window);
        _window.Activate();

        // Handle file activation on cold start
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activatedArgs.Kind == ExtendedActivationKind.File &&
            activatedArgs.Data is IFileActivatedEventArgs fileArgs &&
            fileArgs.Files.Count > 0)
        {
            _window.OpenFileFromActivation(fileArgs.Files[0].Path);
        }

        // Listen for redirected activations (app already running)
        AppInstance.GetCurrent().Activated += OnActivated;
    }

    private void OnActivated(object? sender, AppActivationArguments args)
    {
        if (args.Kind == ExtendedActivationKind.File &&
            args.Data is IFileActivatedEventArgs fileArgs &&
            fileArgs.Files.Count > 0)
        {
            var filePath = fileArgs.Files[0].Path;
            _window?.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                _window?.OpenFileFromActivation(filePath);
                _window?.Activate(); // Bring to front
            });
        }
    }
}
