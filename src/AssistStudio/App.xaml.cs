using AssistStudio.Modules.Helpers;
using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace AssistStudio;

/// <summary>
/// Application entry point that manages the main window lifecycle and file activation handling.
/// </summary>
public partial class App : Application
{
    #region Fields

    /// <summary>
    /// The main application window instance.
    /// </summary>
    private MainWindow? _window;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="App"/> class.
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    #endregion

    #region Overrides

    /// <inheritdoc/>
    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        ConversationManager.Initialize(ApplicationData.Current.LocalFolder.Path);
        await LoggingService.InitializeAsync(ApplicationData.Current.LocalFolder.Path);

        // Wire up Core/Controls diagnostic logging to the app's LoggingService
        DiagnosticLogger.OnException = ex => LoggingService.LogException(ex);
        DiagnosticLogger.OnWarning = msg => LoggingService.LogWarning(msg);

        _window = new MainWindow();

        FieldCure.AssistStudio.Controls.WindowHelper.TrackWindow(_window);
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

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles redirected activation events when the app is already running and a file is opened.
    /// </summary>
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

    #endregion
}
