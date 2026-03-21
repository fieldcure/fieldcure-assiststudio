using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using WindowHelper = FieldCure.AssistStudio.Controls.Helpers.WindowHelper;

namespace AssistStudio;

/// <summary>
/// Application entry point that manages the main window lifecycle and file activation handling.
/// </summary>
public partial class App : Application
{
    #region Fields

    /// <summary>
    /// Gets the main application window instance.
    /// </summary>
    public MainWindow? MainWindow { get; private set; }

    /// <summary>
    /// Gets the app-level MCP server registry singleton.
    /// </summary>
    public static McpServerRegistry McpRegistry { get; } = new();

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
        DiagnosticLogger.OnInfo = msg => LoggingService.LogInfo(msg);

        LoggingService.LogInfo("[App] Startup — services initialized");

        // Initialize MCP server connections (fire-and-forget, failures won't block startup)
        _ = InitializeMcpAsync();

        MainWindow = new MainWindow();

        WindowHelper.TrackWindow(MainWindow);
        MainWindow.Activate();
        LoggingService.LogInfo("[App] MainWindow activated");

        // Handle file activation on cold start
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activatedArgs.Kind == ExtendedActivationKind.File &&
            activatedArgs.Data is IFileActivatedEventArgs fileArgs &&
            fileArgs.Files.Count > 0)
        {
            LoggingService.LogInfo($"[App] Cold-start file activation: {fileArgs.Files[0].Path}");
            MainWindow.OpenFileFromActivation(fileArgs.Files[0].Path);
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
            LoggingService.LogInfo($"[App] Redirected file activation: {filePath}");
            MainWindow?.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
            {
                MainWindow?.OpenFileFromActivation(filePath);
                MainWindow?.Activate(); // Bring to front
            });
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads saved MCP server configurations and connects to enabled servers.
    /// </summary>
    private static async Task InitializeMcpAsync()
    {
        try
        {
            var configs = await AppSettings.LoadMcpServersAsync();
            LoggingService.LogInfo($"[App] Initializing MCP servers ({configs.Count} configs)");
            if (configs.Count > 0)
            {
                var errors = await McpRegistry.ConnectAllAsync(configs);
                foreach (var error in errors)
                    LoggingService.LogWarning($"MCP connect failed: {error}");
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    #endregion
}
