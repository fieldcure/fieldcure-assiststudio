using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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
    private List<string> _currentFolders = [];

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

        try
        {
            var transport = CreateTransport();

            McpClientOptions? options = null;
            if (SupportsRoots)
            {
                lock (_rootsLock)
                {
                    _currentFolders = [.. Config.Arguments ?? []];
                }

                options = new McpClientOptions
                {
                    Capabilities = new ClientCapabilities
                    {
                        Roots = new() { ListChanged = true }
                    },
                    Handlers = new McpClientHandlers
                    {
                        RootsHandler = (_, _) =>
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
                        }
                    }
                };
            }

            _client = await McpClient.CreateAsync(transport, options, cancellationToken: ct);

            // Auto-fill description and version from server's self-reported info
            if (_client.ServerInfo is { } serverInfo)
            {
                if (string.IsNullOrWhiteSpace(Config.Description))
                    Config.Description = serverInfo.Title ?? "";
                ServerVersion = serverInfo.Version;
            }

            var mcpTools = await _client.ListToolsAsync(cancellationToken: ct);
            _mcpTools = [.. mcpTools];
            Tools = [.. mcpTools
                .Select(t => new McpToolAdapter(
                    name: t.Name,
                    description: t.Description ?? string.Empty,
                    parameterSchema: t.JsonSchema.GetRawText(),
                    executeFunc: (args, token) => InvokeMcpToolAsync(t, args, token))
                {
                    ServerName = Config.Name,
                    OverrideRequiresConfirmation = Config.IsBuiltIn
                        ? BuiltInServerHelper.GetRequiresConfirmation(t.Name)
                        : null,
                })];

            State = McpConnectionState.Connected;
            LoggingService.LogInfo($"[MCP] Connected: {Config.Name}, tools={Tools.Count}");
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] Connection failed: {Config.Name} — {ex.Message}");
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
        if (_client is not null)
        {
            try
            {
                _ = _client.DisposeAsync();
            }
            catch { }

            _client = null;
        }

        Tools = [];
        State = McpConnectionState.Disconnected;
    }

    /// <summary>
    /// Asynchronously kills the MCP server process with a timeout-guarded dispose.
    /// </summary>
    public async ValueTask ForceKillAsync()
    {
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

        Tools = [];
        State = McpConnectionState.Disconnected;
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

        LoggingService.LogInfo($"[MCP] Roots updated: {folders.Count} folders");
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

    private IClientTransport CreateTransport()
    {
        return Config.TransportType switch
        {
            McpTransportType.Stdio => new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = Config.Command ?? throw new InvalidOperationException("Stdio transport requires a command."),
                Arguments = Config.Arguments,
                Name = Config.Name,
                EnvironmentVariables = Config.EnvironmentVariables!,
                ShutdownTimeout = TimeSpan.FromSeconds(2),
            }),

            McpTransportType.Http => new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(Config.Url ?? throw new InvalidOperationException("HTTP transport requires a URL.")),
                Name = Config.Name,
            }),

            _ => throw new NotSupportedException($"Transport type {Config.TransportType} is not supported.")
        };
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

    private static async Task<string> InvokeMcpToolAsync(
        McpClientTool mcpTool,
        JsonElement arguments,
        CancellationToken ct)
    {
        var argsDict = ConvertJsonArguments(arguments);
        var result = await mcpTool.CallAsync(argsDict, cancellationToken: ct);
        return ExtractTextResult(result);
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

    private static string ExtractTextResult(ModelContextProtocol.Protocol.CallToolResult result)
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
