using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// Connection state for an MCP server.
/// </summary>
public enum McpConnectionState
{
    /// <summary>Not connected.</summary>
    Disconnected,

    /// <summary>Connection in progress.</summary>
    Connecting,

    /// <summary>Connected and tools are available.</summary>
    Connected,

    /// <summary>Connection failed.</summary>
    Error
}

/// <summary>
/// Represents an active connection to a single MCP server.
/// Manages the <see cref="McpClient"/> lifecycle and provides
/// access to the server's tools as <see cref="IAssistTool"/> instances.
/// </summary>
public partial class McpServerConnection : INotifyPropertyChanged, IAsyncDisposable
{
    #region Fields

    private McpClient? _client;
    private McpConnectionState _state = McpConnectionState.Disconnected;
    private string? _errorMessage;
    private IReadOnlyList<McpToolAdapter> _tools = [];
    private IReadOnlyList<McpClientTool> _mcpTools = [];
    private string? _serverVersion;
    private readonly Lock _rootsLock = new();
    private readonly SemaphoreSlim _toolCallGate = new(1, 1);
    private List<string> _currentFolders = [];
    private readonly Lock _elicitationPresenterLock = new();
    private readonly List<ElicitationPresenterScope> _elicitationPresenterScopes = [];
    private IElicitationPresenter? _activeElicitationPresenter;
    private ElicitationPresenterScope? _activeElicitationPresenterScope;

    /// <summary>
    /// The child <see cref="Process"/> when this connection was launched through
    /// <see cref="IDnxHost"/> (i.e. <c>Config.Command == "dnx"</c>). <see langword="null"/>
    /// for HTTP transports and for legacy stdio servers spawned by the MCP SDK itself.
    /// Owned by this connection — stdin/stdout are wired to the <see cref="StreamClientTransport"/>
    /// and the process is killed/disposed in <see cref="DisposeAsync"/>.
    /// </summary>
    private Process? _dnxProcess;

    /// <summary>
    /// Set to <see langword="true"/> at the start of teardown so the
    /// <see cref="Process.Exited"/> handler can distinguish "we asked for this" from
    /// "the server crashed". Volatile because the handler runs on a thread-pool thread.
    /// </summary>
    private volatile bool _isShuttingDown;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerConnection"/> class.
    /// </summary>
    /// <param name="config">The server configuration.</param>
    public McpServerConnection(McpServerConfig config)
    {
        Config = config;
    }

    #endregion

    #region Properties

    /// <summary>Gets the server configuration.</summary>
    public McpServerConfig Config { get; }

    /// <summary>Gets the current connection state.</summary>
    public McpConnectionState State
    {
        get => _state;
        private set => SetField(ref _state, value);
    }

    /// <summary>Gets the error message if connection failed.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetField(ref _errorMessage, value);
    }

    /// <summary>
    /// Gets the tools provided by this server, wrapped as <see cref="IAssistTool"/>.
    /// Empty until <see cref="ConnectAsync"/> succeeds.
    /// </summary>
    public IReadOnlyList<McpToolAdapter> Tools
    {
        get => _tools;
        private set => SetField(ref _tools, value);
    }

    /// <summary>
    /// Gets the server version reported during MCP handshake, or null if unavailable.
    /// </summary>
    public string? ServerVersion
    {
        get => _serverVersion;
        private set => SetField(ref _serverVersion, value);
    }

    /// <summary>Gets whether this connection is active.</summary>
    public bool IsConnected => State == McpConnectionState.Connected;

    /// <summary>
    /// Gets or sets the name of the tool currently being executed on this connection.
    /// Set automatically during tool invocation so that the elicitation handler
    /// can display the tool name in the UI. <c>null</c> when idle.
    /// </summary>
    public string? CurrentToolName { get; set; }

    /// <summary>Gets or sets the default presenter invoked when the MCP server requests user input.</summary>
    internal IElicitationPresenter? ElicitationPresenter { get; set; }

    /// <summary>
    /// Gets whether this connection uses MCP roots protocol for dynamic folder updates.
    /// When <see langword="true"/>, the client declares roots capability and registers
    /// a handler that returns the current folder list on demand.
    /// </summary>
    public bool SupportsRoots { get; init; }

    #endregion

    #region Methods

    /// <summary>
    /// Connects to the MCP server and retrieves its tool list.
    /// </summary>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        State = McpConnectionState.Connecting;
        ErrorMessage = null;
        LoggingService.LogInfo($"[MCP] Connecting: {Config.Name} ({Config.TransportType})");

        // Measure end-to-end connect latency. The signal we want is "how long
        // did the user wait for this server to come online" — covers transport
        // creation, dnx package fetch on cold cache, McpClient handshake, and
        // ListTools round-trip. All five built-ins kick off in parallel, so
        // the per-connection elapsed reflects real first-launch contention,
        // not isolated package-resolution time.
        var connectSw = Stopwatch.StartNew();

        try
        {
            var transport = await CreateTransportAsync(ct);

            var capabilities = new ClientCapabilities();
            var handlers = new McpClientHandlers();

            // Roots capability
            if (SupportsRoots)
            {
                lock (_rootsLock)
                {
                    _currentFolders = [.. Config.Arguments ?? []];
                }

                capabilities.Roots = new() { ListChanged = true };
                handlers.RootsHandler = (_, _) =>
                {
                    lock (_rootsLock)
                    {
                        var roots = _currentFolders
                            .Select(f => new Root
                            {
                                Uri = new Uri(f).AbsoluteUri,
                                Name = Path.GetFileName(f)
                            })
                            .ToList();
                        return new ValueTask<ListRootsResult>(
                            new ListRootsResult { Roots = roots });
                    }
                };
            }

            // Elicitation capability
            if (ElicitationPresenter is not null)
            {
                capabilities.Elicitation = new();
                handlers.ElicitationHandler = (request, ct) =>
                    HandleElicitationAsync(request, ct);
            }

            var options = new McpClientOptions
            {
                Capabilities = capabilities,
                Handlers = handlers
            };

            _client = await McpClient.CreateAsync(transport, options, cancellationToken: ct);

            // Auto-fill description and version from server's self-reported info.
            // Prefer ServerInfo.Description (purpose/usage text) and fall back to
            // ServerInfo.Title (short brand label) only when Description is blank —
            // matching to Name produces redundant text for the AI-facing field.
            if (_client.ServerInfo is { } serverInfo)
            {
                if (string.IsNullOrWhiteSpace(Config.Description))
                {
                    Config.Description = !string.IsNullOrWhiteSpace(serverInfo.Description)
                        ? serverInfo.Description!
                        : serverInfo.Title ?? "";
                }
                ServerVersion = serverInfo.Version;
            }

            var mcpTools = await _client.ListToolsAsync(cancellationToken: ct);
            _mcpTools = [.. mcpTools];
            Tools = [.. mcpTools
                .Select(t => new McpToolAdapter(
                    name: t.Name,
                    description: t.Description ?? string.Empty,
                    parameterSchema: t.JsonSchema.GetRawText(),
                    executeFunc: (args, token) => InvokeMcpToolAsync(t, t.Name, args, token))
                {
                    ServerName = Config.Name,
                    OverrideRequiresConfirmation = Config.IsBuiltIn
                        ? BuiltInServerHelper.GetRequiresConfirmation(t.Name)
                        : null,
                })];

            State = McpConnectionState.Connected;
            connectSw.Stop();
            LoggingService.LogInfo(
                $"[MCP] Connected: {Config.Name}, tools={Tools.Count}, elapsed={connectSw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            connectSw.Stop();
            LoggingService.LogError(
                $"[MCP] Connection failed: {Config.Name} after {connectSw.ElapsedMilliseconds}ms — {ex.Message}");
            ErrorMessage = ex.Message;
            State = McpConnectionState.Error;
            throw;
        }
    }

    /// <summary>
    /// Disconnects from the MCP server and releases resources.
    /// </summary>
    public async Task DisconnectAsync()
    {
        LoggingService.LogInfo($"[MCP] Disconnecting: {Config.Name}");
        _isShuttingDown = true;
        DetachDnxProcessHandlers();

        if (_client is not null)
        {
            try
            {
                // Give the server a moment to shut down gracefully
                // before the SDK forcefully kills the process
                await _client.DisposeAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) when (
                ex is OperationCanceledException or TimeoutException or Win32Exception)
            {
                // Expected during stdio process teardown — safe to ignore
                LoggingService.LogException(ex);
            }

            _client = null;
        }

        await DisposeDnxProcessAsync(TimeSpan.FromSeconds(2));

        Tools = [];
        _mcpTools = [];
        State = McpConnectionState.Disconnected;
        ErrorMessage = null;
        LoggingService.LogInfo($"[MCP] Disconnected: {Config.Name}");
    }

    /// <summary>
    /// Synchronous force-kill. Prefer <see cref="ForceKillAsync"/> when possible.
    /// Fire-and-forgets DisposeAsync — caller is responsible for process-level cleanup
    /// if orphaned SDK tasks keep the process alive.
    /// </summary>
    public void ForceKill()
    {
        _isShuttingDown = true;
        DetachDnxProcessHandlers();

        if (_client is not null)
        {
            try
            {
                _ = _client.DisposeAsync();
            }
            catch { }

            _client = null;
        }

        if (_dnxProcess is { } proc)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try { proc.Dispose(); } catch { }
            _dnxProcess = null;
        }

        Tools = [];
        State = McpConnectionState.Disconnected;
    }

    /// <summary>
    /// Asynchronously kills the MCP server process with a timeout-guarded dispose.
    /// </summary>
    public async ValueTask ForceKillAsync()
    {
        _isShuttingDown = true;
        DetachDnxProcessHandlers();

        if (_client is not null)
        {
            try
            {
                // SDK ShutdownTimeout is 2 s; allow 1 s margin before giving up.
                await _client.DisposeAsync().AsTask()
                    .WaitAsync(TimeSpan.FromSeconds(3));
            }
            catch { }

            _client = null;
        }

        await DisposeDnxProcessAsync(TimeSpan.FromSeconds(2));

        Tools = [];
        State = McpConnectionState.Disconnected;
    }

    /// <summary>
    /// Detaches the <see cref="OnDnxStderrReceived"/> / <see cref="OnDnxProcessExited"/> handlers
    /// from <see cref="_dnxProcess"/>. Called first in every teardown path so a graceful
    /// shutdown does not surface as a spurious "exited unexpectedly" warning.
    /// </summary>
    private void DetachDnxProcessHandlers()
    {
        if (_dnxProcess is not { } proc) return;
        try { proc.Exited -= OnDnxProcessExited; } catch { }
        try { proc.ErrorDataReceived -= OnDnxStderrReceived; } catch { }
    }

    /// <summary>
    /// Waits up to <paramref name="gracePeriod"/> for the dnx process to exit on its own
    /// (typically driven by stdin EOF after the SDK closes streams), then force-kills the
    /// process tree if it is still alive. Always disposes the <see cref="Process"/> handle
    /// and clears <see cref="_dnxProcess"/>. Idempotent.
    /// </summary>
    private async Task DisposeDnxProcessAsync(TimeSpan gracePeriod)
    {
        if (_dnxProcess is not { } proc) return;
        _dnxProcess = null;

        try
        {
            using var cts = new CancellationTokenSource(gracePeriod);
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
            try { await proc.WaitForExitAsync(); } catch { }
        }
        catch { }

        try { proc.Dispose(); } catch { }
    }

    /// <summary>
    /// Updates the workspace folders and notifies the server via roots protocol.
    /// The server will request the new folder list via <c>roots/list</c> and update
    /// its path validator without restarting.
    /// </summary>
    /// <param name="folders">The new folder list to expose to the server.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task UpdateWorkspaceFoldersAsync(IReadOnlyList<string> folders, CancellationToken ct = default)
    {
        if (!SupportsRoots || _client is null || State != McpConnectionState.Connected)
            return;

        lock (_rootsLock)
        {
            _currentFolders = [.. folders];
        }

        LoggingService.LogInfo($"[MCP] Workspace folders updated: {folders.Count}");
        await _client.SendNotificationAsync("notifications/roots/list_changed", ct);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ForceKillAsync();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Builds the MCP transport for this connection. For stdio servers whose <c>Command</c>
    /// is the <c>"dnx"</c> sentinel, the process is spawned through <see cref="IDnxHost"/>
    /// (embedded ToolHost runner) and wrapped in a <see cref="StreamClientTransport"/>;
    /// for everything else, the SDK's <see cref="StdioClientTransport"/> /
    /// <see cref="HttpClientTransport"/> handles the spawn itself.
    /// </summary>
    private async Task<IClientTransport> CreateTransportAsync(CancellationToken ct)
    {
        switch (Config.TransportType)
        {
            case McpTransportType.Stdio:
                var command = Config.Command
                    ?? throw new InvalidOperationException("Stdio transport requires a command.");

                if (string.Equals(command, "dnx", StringComparison.OrdinalIgnoreCase))
                    return await CreateDnxTransportAsync(ct);

                return new StdioClientTransport(new StdioClientTransportOptions
                {
                    Command = command,
                    Arguments = Config.Arguments,
                    Name = Config.Name,
                    EnvironmentVariables = Config.EnvironmentVariables!,
                    ShutdownTimeout = TimeSpan.FromSeconds(2),
                });

            case McpTransportType.Http:
                return new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(Config.Url
                        ?? throw new InvalidOperationException("HTTP transport requires a URL.")),
                    Name = Config.Name,
                });

            default:
                throw new NotSupportedException($"Transport type {Config.TransportType} is not supported.");
        }
    }

    /// <summary>
    /// Launches the configured tool through <see cref="IDnxHost"/> and wraps its stdio in a
    /// <see cref="StreamClientTransport"/>. Wires <see cref="Process.ErrorDataReceived"/>
    /// (forwarded to <see cref="LoggingService"/>) and <see cref="Process.Exited"/>
    /// (logged as a crash unless <see cref="_isShuttingDown"/> is set) before returning.
    /// </summary>
    private async Task<IClientTransport> CreateDnxTransportAsync(CancellationToken ct)
    {
        var spec = BuiltInServerHelper.ParseDnxInvocation(
            Config.Arguments,
            contextLabel: $"MCP:{Config.Name}");

        var dnxHost = App.Services.GetRequiredService<IDnxHost>();

        IReadOnlyDictionary<string, string?>? env = null;
        if (Config.EnvironmentVariables is { Count: > 0 } envVars)
        {
            var dict = new Dictionary<string, string?>(envVars.Count, StringComparer.Ordinal);
            foreach (var kvp in envVars)
                dict[kvp.Key] = kvp.Value;
            env = dict;
        }

        _dnxProcess = await dnxHost.StartAsync(
            spec.PackageId,
            spec.VersionRange,
            spec.ToolArguments,
            env,
            ct);

        LoggingService.LogInfo(
            $"[MCP] Launched via IDnxHost: {Config.Name} pkg={spec.PackageId}" +
            $"@{spec.VersionRange ?? "latest"} pid={_dnxProcess.Id}");

        _dnxProcess.ErrorDataReceived += OnDnxStderrReceived;
        _dnxProcess.BeginErrorReadLine();
        _dnxProcess.EnableRaisingEvents = true;
        _dnxProcess.Exited += OnDnxProcessExited;

        return new StreamClientTransport(
            serverInput: _dnxProcess.StandardInput.BaseStream,
            serverOutput: _dnxProcess.StandardOutput.BaseStream,
            loggerFactory: NullLoggerFactory.Instance);
    }

    /// <summary>
    /// Forwards <c>stderr</c> from the dnx-launched MCP server to the app log. Without this,
    /// server startup errors (missing API keys, malformed config, native dependency load
    /// failures) would vanish silently because the SDK only reads <c>stdout</c>.
    /// </summary>
    private void OnDnxStderrReceived(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null) return;
        LoggingService.LogWarning($"[MCP:{Config.Name}] stderr: {e.Data}");
    }

    /// <summary>
    /// Handler for the dnx process's <see cref="Process.Exited"/> event. Suppressed during
    /// expected teardown (<see cref="_isShuttingDown"/> is set first in <see cref="DisposeAsync"/>);
    /// otherwise logs the unexpected exit so the user sees why their MCP server stopped.
    /// </summary>
    private void OnDnxProcessExited(object? sender, EventArgs e)
    {
        if (_isShuttingDown) return;

        int? code = null;
        try { code = _dnxProcess?.ExitCode; } catch { }
        LoggingService.LogWarning(
            $"[MCP:{Config.Name}] Server process exited unexpectedly (code={code?.ToString() ?? "?"}).");
    }

    /// <summary>
    /// Calls an MCP tool by name with progress notification support.
    /// Use this instead of <see cref="IAssistTool.ExecuteAsync"/> when you need
    /// to receive progress updates (e.g., indexing operations).
    /// </summary>
    /// <param name="toolName">The name of the tool to call.</param>
    /// <param name="arguments">JSON arguments to pass to the tool.</param>
    /// <param name="progress">Optional progress callback receiving (current, total, message).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The tool result as a string.</returns>
    public async Task<string> CallToolWithProgressAsync(
        string toolName,
        JsonElement arguments,
        IProgress<(double Current, double Total, string? Message)>? progress,
        CancellationToken ct = default)
    {
        await _toolCallGate.WaitAsync(ct);
        var previousToolName = CurrentToolName;
        var previousPresenter = ActivateCurrentElicitationPresenterScope();

        try
        {
            CurrentToolName = toolName;

            var mcpTool = _mcpTools.FirstOrDefault(t => t.Name == toolName)
                ?? throw new InvalidOperationException($"Tool '{toolName}' not found.");

            var argsDict = ConvertJsonArguments(arguments);

            IProgress<ModelContextProtocol.ProgressNotificationValue>? mcpProgress = null;
            if (progress is not null)
            {
                mcpProgress = new Progress<ModelContextProtocol.ProgressNotificationValue>(value =>
                {
                    progress.Report((value.Progress, value.Total ?? 0, value.Message));
                });
            }

            var result = await mcpTool.CallAsync(argsDict, progress: mcpProgress, cancellationToken: ct);
            return ExtractTextResult(result);
        }
        finally
        {
            RestoreActiveElicitationPresenter(previousPresenter);
            CurrentToolName = previousToolName;
            _toolCallGate.Release();
        }
    }

    /// <summary>
    /// Pushes a temporary elicitation presenter for the current logical operation.
    /// Dispose the returned scope to restore the previous presenter.
    /// </summary>
    internal ElicitationPresenterScope PushElicitationPresenter(IElicitationPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(presenter);

        var scope = new ElicitationPresenterScope(this, presenter);
        lock (_elicitationPresenterLock)
        {
            _elicitationPresenterScopes.Add(scope);
        }

        return scope;
    }

    /// <summary>Removes a presenter scope that was previously pushed.</summary>
    internal void PopElicitationPresenter(ElicitationPresenterScope scope)
    {
        lock (_elicitationPresenterLock)
        {
            _elicitationPresenterScopes.Remove(scope);
        }
    }

    /// <summary>
    /// Dispatches an MCP elicitation request to the active presenter.
    /// </summary>
    private async ValueTask<ElicitResult> HandleElicitationAsync(
        ElicitRequestParams? request,
        CancellationToken ct)
    {
        if (request is null)
            return new ElicitResult { Action = "cancel" };

        var (presenter, scope) = GetActiveElicitationPresenter();
        if (presenter is null)
            return new ElicitResult { Action = "cancel" };

        var elicitationRequest = new ElicitationRequest(
            CurrentToolName ?? Config.Name,
            Config.Name,
            request.Message,
            McpElicitationMapper.ConvertSchema(request.RequestedSchema));

        var result = await presenter.PresentAsync(elicitationRequest, ct);
        if (IsElicitationCancelled(result.Action))
            scope?.MarkCancelled();

        return result;
    }

    /// <summary>Captures the top scoped presenter as active for the current tool call.</summary>
    private (IElicitationPresenter? Presenter, ElicitationPresenterScope? Scope) ActivateCurrentElicitationPresenterScope()
    {
        lock (_elicitationPresenterLock)
        {
            var previous = (_activeElicitationPresenter, _activeElicitationPresenterScope);
            var scope = _elicitationPresenterScopes.Count > 0
                ? _elicitationPresenterScopes[^1]
                : null;

            _activeElicitationPresenter = scope?.Presenter;
            _activeElicitationPresenterScope = scope;
            return previous;
        }
    }

    /// <summary>Restores the active presenter after a tool call completes.</summary>
    private void RestoreActiveElicitationPresenter(
        (IElicitationPresenter? Presenter, ElicitationPresenterScope? Scope) previous)
    {
        lock (_elicitationPresenterLock)
        {
            _activeElicitationPresenter = previous.Presenter;
            _activeElicitationPresenterScope = previous.Scope;
        }
    }

    /// <summary>Gets the active call presenter, or the default presenter if no scope is active.</summary>
    private (IElicitationPresenter? Presenter, ElicitationPresenterScope? Scope) GetActiveElicitationPresenter()
    {
        lock (_elicitationPresenterLock)
        {
            return _activeElicitationPresenter is null
                ? (ElicitationPresenter, null)
                : (_activeElicitationPresenter, _activeElicitationPresenterScope);
        }
    }

    /// <summary>Returns true when an elicitation result ended without accepted content.</summary>
    private static bool IsElicitationCancelled(string? action) =>
        string.Equals(action, "cancel", StringComparison.OrdinalIgnoreCase)
        || string.Equals(action, "decline", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Invokes an MCP tool and extracts both text and media content from the result.
    /// </summary>
    private async Task<ToolExecutionResult> InvokeMcpToolAsync(
        McpClientTool mcpTool,
        string toolName,
        JsonElement arguments,
        CancellationToken ct)
    {
        await _toolCallGate.WaitAsync(ct);
        var previousToolName = CurrentToolName;
        var previousPresenter = ActivateCurrentElicitationPresenterScope();

        try
        {
            CurrentToolName = toolName;
            var argsDict = ConvertJsonArguments(arguments);
            var result = await mcpTool.CallAsync(argsDict, cancellationToken: ct);
            var text = ExtractTextResult(result);
            var media = ExtractMediaContents(result);
            // structuredContent is a host-side rendering channel (not fed to the
            // model). The chat panel inspects it for an inline chart spec.
            return new ToolExecutionResult(text, media, result.StructuredContent);
        }
        finally
        {
            RestoreActiveElicitationPresenter(previousPresenter);
            CurrentToolName = previousToolName;
            _toolCallGate.Release();
        }
    }

    private static Dictionary<string, object?> ConvertJsonArguments(JsonElement arguments)
    {
        var argsDict = new Dictionary<string, object?>();
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in arguments.EnumerateObject())
            {
                argsDict[prop.Name] = ConvertJsonElement(prop.Value);
            }
        }
        return argsDict;
    }

    private static string ExtractTextResult(CallToolResult result)
    {
        if (result.Content is { Count: > 0 } content)
        {
            var texts = content
                .Where(c => c is TextContentBlock)
                .Select(c => ((TextContentBlock)c).Text);
            return string.Join("\n", texts);
        }
        return result.IsError == true ? """{"error": true}""" : "{}";
    }

    /// <summary>
    /// Extracts media content from an MCP tool result.
    /// Handles ImageContentBlock, AudioContentBlock, and EmbeddedResourceBlock.
    /// </summary>
    private static IReadOnlyList<MediaContent>? ExtractMediaContents(CallToolResult result)
    {
        if (result.Content is not { Count: > 0 } content)
            return null;

        List<MediaContent>? media = null;

        foreach (var block in content)
        {
            switch (block)
            {
                // ImageContentBlock — SDK 1.2: Data is base64-encoded UTF-8 bytes
                case ImageContentBlock image:
                {
                    var base64 = Encoding.UTF8.GetString(image.Data.Span);
                    var dataUri = $"data:{image.MimeType};base64,{base64}";
                    (media ??= []).Add(new MediaContent(dataUri, image.MimeType, MediaContentKind.Image));
                    break;
                }

                // AudioContentBlock — SDK 1.2: Data is base64-encoded UTF-8 bytes
                case AudioContentBlock audio:
                {
                    var base64 = Encoding.UTF8.GetString(audio.Data.Span);
                    var dataUri = $"data:{audio.MimeType};base64,{base64}";
                    (media ??= []).Add(new MediaContent(dataUri, audio.MimeType, MediaContentKind.Audio));
                    break;
                }

                // EmbeddedResourceBlock — resource with optional blob
                case EmbeddedResourceBlock resource:
                {
                    var extracted = ExtractFromResource(resource);
                    if (extracted is not null)
                        (media ??= []).Add(extracted);
                    break;
                }
            }
        }

        return media;
    }

    /// <summary>
    /// Extracts media content from an EmbeddedResourceBlock.
    /// Handles blob-included and URI-only resource forms.
    /// </summary>
    private static MediaContent? ExtractFromResource(EmbeddedResourceBlock resource)
    {
        var mimeType = resource.Resource?.MimeType;
        if (string.IsNullOrEmpty(mimeType))
            return null;

        // Skip text resources — handled by ExtractTextResult
        if (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
            return null;

        var kind = ClassifyMimeType(mimeType);

        // Form 1: Blob included (BlobResourceContents)
        // SDK 1.2: Blob is base64-encoded UTF-8 bytes (same pattern as ImageContentBlock.Data)
        if (resource.Resource is BlobResourceContents blobResource
            && blobResource.Blob.Length > 0)
        {
            var base64 = Encoding.UTF8.GetString(blobResource.Blob.Span);
            var dataUri = $"data:{mimeType};base64,{base64}";
            return new MediaContent(dataUri, mimeType, kind);
        }

        // Form 2: URI only — pass through for downstream handling
        var uri = resource.Resource?.Uri;
        if (uri is null)
            return null;

        if (uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var path = new Uri(uri).LocalPath;
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    var base64 = Convert.ToBase64String(bytes);
                    var dataUri = $"data:{mimeType};base64,{base64}";
                    return new MediaContent(dataUri, mimeType, kind);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(
                    $"[MCP] Failed to read resource file: {ex.Message}");
            }

            return null;
        }

        // http(s):// URI — return as-is for link rendering
        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return new MediaContent(uri, mimeType, kind);
        }

        return null;
    }

    /// <summary>
    /// Classifies a MIME type into a rendering category.
    /// </summary>
    private static MediaContentKind ClassifyMimeType(string? mimeType)
    {
        if (string.IsNullOrEmpty(mimeType))
            return MediaContentKind.Download;

        if (mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return MediaContentKind.Image;
        if (mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
            return MediaContentKind.Audio;
        if (mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
            return MediaContentKind.Video;

        return MediaContentKind.Download;
    }

    private static object? ConvertJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
        _ => element.GetRawText(),
    };

    #endregion

    #region INotifyPropertyChanged

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    #endregion
}
