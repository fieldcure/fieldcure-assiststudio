using System.Runtime.InteropServices;
using System.Web;
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
/// Application entry point that manages the main window lifecycle, file activation,
/// and protocol activation (<c>assiststudio://</c>) handling.
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

        // Handle cold-start activation (file or protocol)
        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        HandleActivation(activatedArgs);

        // Listen for redirected activations (app already running)
        AppInstance.GetCurrent().Activated += OnActivated;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles redirected activation events when the app is already running.
    /// </summary>
    private void OnActivated(object? sender, AppActivationArguments args)
    {
        LoggingService.LogInfo($"[App] Redirected activation: {args.Kind}");
        HandleActivation(args);
    }

    #endregion

    #region Activation Handling

    /// <summary>
    /// Processes activation arguments from protocol, file, or launch activation.
    /// Called both on initial launch and when redirected from another instance.
    /// </summary>
    private void HandleActivation(AppActivationArguments args)
    {
        switch (args.Kind)
        {
            case ExtendedActivationKind.File:
                if (args.Data is IFileActivatedEventArgs fileArgs && fileArgs.Files.Count > 0)
                {
                    LoggingService.LogInfo($"[App] File activation: {fileArgs.Files[0].Path}");
                    OpenFileOnUiThread(fileArgs.Files[0].Path);
                }
                break;

            case ExtendedActivationKind.Protocol:
                if (args.Data is IProtocolActivatedEventArgs protocolArgs)
                {
                    LoggingService.LogInfo($"[App] Protocol activation: {protocolArgs.Uri}");
                    HandleProtocolActivation(protocolArgs.Uri);
                }
                break;

            case ExtendedActivationKind.Launch:
                // Normal launch — nothing special
                break;
        }
    }

    /// <summary>
    /// Handles activation via <c>assiststudio://</c> protocol URI.
    /// Bare URI brings the window to front; <c>assiststudio://open?file=...</c> opens a file.
    /// </summary>
    private void HandleProtocolActivation(Uri uri)
    {
        if (uri.Host.Equals("open", StringComparison.OrdinalIgnoreCase))
        {
            var query = HttpUtility.ParseQueryString(uri.Query);
            var filePath = query["file"];
            if (!string.IsNullOrEmpty(filePath))
            {
                OpenFileOnUiThread(filePath);
                return;
            }
        }

        // assiststudio:// (bare) or unrecognized command — bring window to front
        BringWindowToFront();
    }

    /// <summary>
    /// Opens a conversation file on the UI thread, bringing the window to front.
    /// </summary>
    private void OpenFileOnUiThread(string filePath)
    {
        if (!File.Exists(filePath) ||
            !filePath.EndsWith(ConversationManager.FileExtension, StringComparison.OrdinalIgnoreCase))
        {
            LoggingService.LogWarning($"[App] Invalid activation file: {filePath}");
            return;
        }

        MainWindow?.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            MainWindow?.OpenFileFromActivation(filePath);
            BringWindowToFrontCore();
        });
    }

    /// <summary>
    /// Brings the main window to the foreground, restoring it if minimized.
    /// </summary>
    private void BringWindowToFront()
    {
        MainWindow?.DispatcherQueue.TryEnqueue(BringWindowToFrontCore);
    }

    /// <summary>
    /// Core implementation for bringing the window to front. Must be called on the UI thread.
    /// </summary>
    private void BringWindowToFrontCore()
    {
        if (MainWindow is null) return;
        MainWindow.Activate();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads saved MCP server configurations and connects to enabled servers.
    /// </summary>
    private static async Task InitializeMcpAsync()
    {
        try
        {
            // Ensure built-in server tools are installed via dotnet tool
            await BuiltInServerHelper.EnsureInstalledAsync();

            var configs = await AppSettings.LoadMcpServersAsync();
            LoggingService.LogInfo($"[App] Initializing MCP servers ({configs.Count} configs)");
            if (configs.Count > 0)
            {
                var errors = await McpRegistry.ConnectAllAsync(configs);
                foreach (var error in errors)
                    LoggingService.LogWarning($"MCP connect failed: {error}");
            }

            // Connect built-in servers
            var builtInConfigs = AppSettings.BuiltInServers;
            foreach (var (key, config) in builtInConfigs)
            {
                if (config.IsEnabled && config.Folders.Count > 0)
                    await McpRegistry.ConnectBuiltInAsync(key, config);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    #endregion
}
