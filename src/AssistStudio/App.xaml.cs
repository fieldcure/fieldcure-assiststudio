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
using FieldCure.Ai.Providers;
using FieldCure.AssistStudio.Controls;
using FieldCure.DocumentParsers.Pdf;
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
        DocumentParserFactoryExtensions.AddPdfSupport();
        ConversationManager.Initialize(ApplicationData.Current.LocalFolder.Path);
        AppSettings.MigrateAppTasksSettings();

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

        // Wire elicitation handler before connecting MCP servers
        McpRegistry.ElicitationHandler = HandleElicitationAsync;

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
            // Sync API keys from UWP PasswordVault to Win32 Credential Manager
            // so external processes (Runner serve/exec) can access them
            PasswordVaultHelper.SyncToCredentialManager();

            // Connect external (user-configured) MCP servers first — these are
            // independent of built-in server updates and can start immediately.
            var configs = await AppSettings.LoadMcpServersAsync();
            LoggingService.LogInfo($"[App] Initializing MCP servers ({configs.Count} configs)");
            var externalTask = configs.Count > 0
                ? McpRegistry.ConnectAllAsync(configs)
                : Task.FromResult<IReadOnlyList<string>>([]);

            // Install missing tools + apply pending updates (fast path: typically 0ms)
            try { await BuiltInServerHelper.InitializeToolsAsync(); }
            catch (Exception ex) { LoggingService.LogWarning($"[BuiltIn] Initialization failed: {ex.Message}"); }

            // Start built-in servers (filesystem is per-tab, skip here)
            var builtInConfigs = AppSettings.BuiltInServers;
            var builtInTasks = builtInConfigs
                .Where(kv => kv.Key != BuiltInServerHelper.FilesystemKey
                             && kv.Value.IsEnabled
                             && (BuiltInServerHelper.IsSharedServer(kv.Key) || kv.Value.Folders.Count > 0))
                .Select(kv => McpRegistry.ConnectBuiltInAsync(kv.Key, kv.Value));
            await Task.WhenAll(builtInTasks);

            // Background: check NuGet for newer versions (fire-and-forget)
            _ = Task.Run(BuiltInServerHelper.CheckForUpdatesInBackgroundAsync);

            // Report external server connection errors
            var errors = await externalTask;
            foreach (var error in errors)
                LoggingService.LogWarning($"MCP connect failed: {error}");
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
        }
    }

    #endregion

    #region Elicitation

    /// <summary>
    /// Handles MCP server elicitation requests by routing them to the active ChatPanel.
    /// </summary>
    private static async ValueTask<ModelContextProtocol.Protocol.ElicitResult> HandleElicitationAsync(
        McpServerConnection connection,
        ModelContextProtocol.Protocol.ElicitRequestParams request,
        CancellationToken ct)
    {
        var app = (App)Current;
        var panel = (app.MainWindow as MainWindow)?.ViewModel.SelectedTab?.Panel;
        if (panel is null)
            return new ModelContextProtocol.Protocol.ElicitResult { Action = "cancel" };

        var fields = ConvertSchema(request.RequestedSchema);
        var toolName = connection.CurrentToolName ?? connection.Config.Name;
        var serverName = connection.Config.Name;

        // Marshal to UI thread and await panel result
        var tcs = new TaskCompletionSource<(string Action, IDictionary<string, object?>? Content)>();
        panel.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var result = await panel.RequestElicitationAsync(toolName, serverName, request.Message, fields);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });

        var (action, content) = await tcs.Task;
        return ConvertToElicitResult(action, content);
    }

    /// <summary>
    /// Converts an MCP <see cref="ModelContextProtocol.Protocol.ElicitRequestParams.RequestSchema"/>
    /// to a list of <see cref="ElicitationFieldInfo"/> for the panel.
    /// </summary>
    private static List<ElicitationFieldInfo> ConvertSchema(
        ModelContextProtocol.Protocol.ElicitRequestParams.RequestSchema? schema)
    {
        var fields = new List<ElicitationFieldInfo>();
        if (schema?.Properties is null) return fields;

        foreach (var (name, definition) in schema.Properties)
        {
            var field = definition switch
            {
                ModelContextProtocol.Protocol.ElicitRequestParams.UntitledSingleSelectEnumSchema enumSchema =>
                    new ElicitationFieldInfo
                    {
                        Name = name,
                        Type = ElicitationFieldType.Enum,
                        Title = enumSchema.Title,
                        Description = enumSchema.Description,
                        DefaultValue = enumSchema.Default,
                        Options = enumSchema.Enum
                            .Select(v => new ElicitationOptionInfo { Value = v, DisplayTitle = v })
                            .ToList()
                    },

                ModelContextProtocol.Protocol.ElicitRequestParams.TitledSingleSelectEnumSchema titledSchema =>
                    new ElicitationFieldInfo
                    {
                        Name = name,
                        Type = ElicitationFieldType.Enum,
                        Title = titledSchema.Title,
                        Description = titledSchema.Description,
                        DefaultValue = titledSchema.Default,
                        Options = titledSchema.OneOf
                            .Select(o => new ElicitationOptionInfo { Value = o.Const, DisplayTitle = o.Title })
                            .ToList()
                    },

                ModelContextProtocol.Protocol.ElicitRequestParams.BooleanSchema boolSchema =>
                    new ElicitationFieldInfo
                    {
                        Name = name,
                        Type = ElicitationFieldType.Boolean,
                        Title = boolSchema.Title,
                        Description = boolSchema.Description,
                        DefaultValue = boolSchema.Default?.ToString(),
                        Options =
                        [
                            new() { Value = "true", DisplayTitle = "Yes" },
                            new() { Value = "false", DisplayTitle = "No" }
                        ]
                    },

                // StringSchema, NumberSchema, and any unknown types → free-text input
                _ => new ElicitationFieldInfo
                {
                    Name = name,
                    Type = ElicitationFieldType.String,
                    Title = definition.Title,
                    Description = definition.Description,
                }
            };

            fields.Add(field);
        }

        return fields;
    }

    /// <summary>
    /// Converts the panel result to an MCP <see cref="ModelContextProtocol.Protocol.ElicitResult"/>.
    /// </summary>
    private static ModelContextProtocol.Protocol.ElicitResult ConvertToElicitResult(
        string action, IDictionary<string, object?>? content)
    {
        if (action != "accept" || content is null)
            return new ModelContextProtocol.Protocol.ElicitResult { Action = action };

        var jsonContent = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var (key, value) in content)
        {
            jsonContent[key] = value switch
            {
                "true" => System.Text.Json.JsonSerializer.SerializeToElement(true),
                "false" => System.Text.Json.JsonSerializer.SerializeToElement(false),
                string s => System.Text.Json.JsonSerializer.SerializeToElement(s),
                _ => System.Text.Json.JsonSerializer.SerializeToElement(value?.ToString() ?? "")
            };
        }

        return new ModelContextProtocol.Protocol.ElicitResult
        {
            Action = "accept",
            Content = jsonContent
        };
    }

    #endregion
}
