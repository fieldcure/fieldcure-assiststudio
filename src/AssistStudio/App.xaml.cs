using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.Ai.Providers;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Core.Helpers;
using FieldCure.DocumentParsers.Imaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Runtime.InteropServices;
using System.Web;
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
    /// Resource loader used by <see cref="InitializeMcpAsync"/> to localize the
    /// unified MCP-startup infobar (Mcp_ServersConnecting / Mcp_ServersConnected /
    /// Mcp_ConnectionFailed). Static so the async startup helper can read it
    /// without an instance reference.
    /// </summary>
    private static readonly ResourceLoader _loader = new();

    /// <summary>
    /// Gets the app-level MCP server registry singleton.
    /// </summary>
    public static McpServerRegistry McpRegistry { get; } = new();

    /// <summary>
    /// App-wide service provider. Set in <see cref="OnLaunched"/> and used as a service-locator
    /// from <see cref="McpServerConnection"/> and <see cref="RagProcessManager"/> for the
    /// embedded <see cref="IDnxHost"/> (built-in MCP server launcher).
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    // Memory is now managed by Essentials MCP server (memory.db)

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
        DocumentParserFactoryImagingExtensions.AddImagingSupport();
        ConversationManager.Initialize(ApplicationData.Current.LocalFolder.Path);
        AppSettings.MigrateAppTasksSettings();
        AppSettings.MigrateAuxiliaryModelKeys();

        // Clean up leftover temp media from previous sessions (e.g. abnormal shutdown)
        ChatPanel.CleanupTempMedia();
        await LoggingService.InitializeAsync(ApplicationData.Current.LocalFolder.Path);

        // Wire up Core/Controls diagnostic logging to the app's LoggingService
        DiagnosticLogger.OnException = ex => LoggingService.LogException(ex);
        DiagnosticLogger.OnWarning = msg => LoggingService.LogWarning(msg);
        DiagnosticLogger.OnInfo = msg => LoggingService.LogInfo(msg);

        // Load custom provider configs and register with factory
        foreach (var config in AppSettings.LoadCustomProviders())
            ProviderFactory.RegisterCustomProvider(config);

        LoggingService.LogInfo("[App] Startup — services initialized");

        // Build the DI container and initialize the embedded NuGet tool host. All built-in
        // MCP servers (Filesystem/RAG/Outbox/Runner/Essentials) and the RAG queue
        // orchestrator are launched through IDnxHost — replacing the prior dependency on
        // the .NET 10 SDK's `dnx` command, which is unavailable on MS Store installs.
        // The init is awaited synchronously here so downstream callers can rely on a ready
        // host without their own retry/wait logic.
        Services = BuildServices();
        try
        {
            await Services.GetRequiredService<IDnxHost>().InitializeAsync();
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[App] DnxHost initialization failed: {ex.Message}");
            ShowFatalStartupError(
                "AssistStudio could not start.\n\n" +
                "The .NET runtime is required but was not found on this system.\n\n" +
                $"Details: {ex.Message}");
            Exit();
            return;
        }

        // Wire elicitation presenter before connecting MCP servers.
        McpRegistry.ElicitationPresenter = new ChatPanelElicitationPresenter(() =>
        {
            var app = (App)Current;
            return (app.MainWindow as MainWindow)?.ViewModel.SelectedTab?.Panel;
        });

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    /// <summary>
    /// Shows a fatal startup error via Win32 <c>MessageBox</c>. Used during the brief window
    /// after activation but before <see cref="MainWindow"/> exists (so no XamlRoot is yet
    /// available for a WinUI <c>ContentDialog</c>).
    /// </summary>
    private static void ShowFatalStartupError(string message)
    {
        const uint MB_OK = 0x0;
        const uint MB_ICONERROR = 0x10;
        _ = MessageBoxW(nint.Zero, message, "AssistStudio", MB_OK | MB_ICONERROR);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Builds the app-wide <see cref="IServiceProvider"/>. Loads <c>appsettings.json</c> from
    /// the install folder, binds <see cref="ToolHostOptions"/>, and registers
    /// <see cref="IDnxHost"/> as a singleton.
    /// </summary>
    private static IServiceProvider BuildServices()
    {
        var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var toolHostOptions = ToolHostOptions.LoadFromJson(appSettingsPath);

        var services = new ServiceCollection();
        services.AddSingleton(toolHostOptions);
        services.AddSingleton<IDnxHost, DnxHost>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Loads saved MCP server configurations and connects to enabled servers.
    /// External and built-in connects run in parallel under a single combined
    /// "Connecting MCP servers (N)…" infobar so the user sees one start-to-finish
    /// notification regardless of which batch the servers belong to. The infobar
    /// covers the common case (zero external + a few built-in), where the prior
    /// per-batch pattern left the user with a silent 7–15 s window during the
    /// dnx fetch / serve start.
    /// </summary>
    private static async Task InitializeMcpAsync()
    {
        try
        {
            // Sync API keys from UWP PasswordVault to Win32 Credential Manager
            // so external processes (Runner serve/exec) can access them
            PasswordVaultHelper.SyncToCredentialManager();

            var configs = await AppSettings.LoadMcpServersAsync();
            var builtInConfigs = AppSettings.BuiltInServers;
            LoggingService.LogInfo($"[App] Initializing MCP servers ({configs.Count} configs)");

            // Pre-count what we will actually try to connect — must mirror the
            // filter predicates used below for ConnectAllAsync (IsEnabled) and
            // ConnectBuiltInAsync (key != filesystem AND IsEnabled AND (shared
            // OR has folders)). The total feeds the unified "connecting" infobar.
            var enabledExternalCount = configs.Count(c => c.IsEnabled);
            var enabledBuiltInCount = builtInConfigs.Count(kv =>
                kv.Key != BuiltInServerHelper.FilesystemKey
                && kv.Value.IsEnabled
                && (BuiltInServerHelper.IsSharedServer(kv.Key) || kv.Value.Folders.Count > 0));
            var totalCount = enabledExternalCount + enabledBuiltInCount;

            // Single persistent infobar covering both batches. Dismissed only
            // after BOTH WhenAll completions; the success / warning summary is
            // posted by this method, not by McpServerRegistry — that registry
            // intentionally returns errors and posts no UI of its own.
            var connectingToken = Guid.Empty;
            if (totalCount > 0)
            {
                connectingToken = NotificationCenter.Instance.PostPersistent(
                    InfoBarSeverity.Informational,
                    string.Format(_loader.GetString("Mcp_ServersConnecting"), totalCount),
                    string.Empty);
            }

            // Auto-update external servers installed as global .NET tools BEFORE
            // connecting, so a newer binary is picked up and we avoid file-lock
            // races against a running subprocess. Fire-and-forget via await: this
            // is a parallel sweep and typically completes well under a second when
            // tools are already at latest; network issues log a warning but do not
            // block.
            if (enabledExternalCount > 0)
                await ExternalDotnetToolUpdater.CheckAndUpdateAsync(configs);

            var externalTask = enabledExternalCount > 0
                ? McpRegistry.ConnectAllAsync(configs)
                : Task.FromResult<IReadOnlyList<string>>([]);

            // Install missing tools + apply pending updates (fast path: typically 0ms)
            try { await BuiltInServerHelper.InitializeToolsAsync(); }
            catch (Exception ex) { LoggingService.LogWarning($"[BuiltIn] Initialization failed: {ex.Message}"); }

            // One-shot retirement of the pre-dnx tools/ folder. Runner v2.0.1+
            // resolves itself and its stateless MCP servers through dnx, so this
            // folder is now pure dead weight; leaving stale binaries behind would
            // cause version-skew failures.
            _ = Task.Run(BuiltInServerHelper.RemoveLegacyToolPathFolderAsync);

            // Start built-in servers (filesystem is per-tab, skip here).
            // Orphan KB folder cleanup runs inside RAG serve at startup as of
            // FieldCure.Mcp.Rag v2.4.4 — folding it into serve removed the
            // dnx fetch-lock race that the prior separate prune-orphans
            // process triggered on cold caches.
            // Materialized to List so the tasks actually fire here (Select is
            // lazy; WhenAll's enumeration would still trigger them, but ToList
            // makes the parallel-launch point explicit and matches the rest of
            // this method's "fire then await" structure).
            var builtInTasks = builtInConfigs
                .Where(kv => kv.Key != BuiltInServerHelper.FilesystemKey
                             && kv.Value.IsEnabled
                             && (BuiltInServerHelper.IsSharedServer(kv.Key) || kv.Value.Folders.Count > 0))
                .Select(kv => McpRegistry.ConnectBuiltInAsync(kv.Key, kv.Value))
                .ToList();
            var builtInResults = await Task.WhenAll(builtInTasks);

            // Background: check NuGet for newer versions (fire-and-forget)
            _ = Task.Run(BuiltInServerHelper.CheckForUpdatesInBackgroundAsync);

            var externalErrors = await externalTask;

            // Combine errors from both batches into the same "name: message"
            // shape so the failure infobar can render them with the same
            // .Split(':')[0] name extractor.
            var allErrors = externalErrors
                .Concat(builtInResults.OfType<string>())
                .ToList();

            if (connectingToken != Guid.Empty)
                NotificationCenter.Instance.Dismiss(connectingToken);

            var connected = totalCount - allErrors.Count;
            if (totalCount > 0 && connected > 0)
            {
                NotificationCenter.Instance.Post(
                    InfoBarSeverity.Success,
                    string.Format(_loader.GetString("Mcp_ServersConnected"), connected),
                    string.Empty);
            }
            if (allErrors.Count > 0)
            {
                NotificationCenter.Instance.Post(
                    InfoBarSeverity.Warning,
                    _loader.GetString("Mcp_ConnectionFailed"),
                    string.Join(", ", allErrors.Select(e => e.Split(':')[0])),
                    5000);
            }

            foreach (var error in externalErrors)
                LoggingService.LogWarning($"MCP connect failed: {error}");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    #endregion

}
