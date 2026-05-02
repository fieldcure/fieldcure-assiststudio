using AssistStudio.Helpers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;

namespace AssistStudio.Mcp;

/// <summary>
/// Manages all MCP server connections at the application level.
/// Singleton lifetime — survives profile switches and conversation changes.
/// </summary>
public class McpServerRegistry : IAsyncDisposable
{
    #region Fields

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ObservableCollection<McpServerConnection> _connections = [];
    private readonly ResourceLoader _loader = new();

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the tool list changes (server connected/disconnected).
    /// </summary>
    public event EventHandler? ToolsChanged;

    /// <summary>
    /// Gets or sets the default elicitation presenter propagated to all new connections.
    /// Set this before calling <see cref="ConnectAllAsync"/> or <see cref="ConnectServerAsync"/>.
    /// </summary>
    internal IElicitationPresenter? ElicitationPresenter { get; set; }

    #endregion

    #region Properties

    /// <summary>Gets all connections (observable for UI binding).</summary>
    public ReadOnlyObservableCollection<McpServerConnection> Connections { get; }

    /// <summary>
    /// Gets all available MCP tools across all connected and enabled servers.
    /// </summary>
    public IReadOnlyList<McpToolAdapter> AllTools
        => [.. _connections
            .Where(c => c.IsConnected && c.Config.IsEnabled)
            .SelectMany(c => c.Tools)];

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerRegistry"/> class.
    /// </summary>
    public McpServerRegistry()
    {
        Connections = new ReadOnlyObservableCollection<McpServerConnection>(_connections);
    }

    /// <summary>Creates a new connection and propagates the elicitation presenter.</summary>
    private McpServerConnection CreateConnection(McpServerConfig config, bool supportsRoots = false)
    {
        var connection = new McpServerConnection(config)
        {
            SupportsRoots = supportsRoots,
            ElicitationPresenter = ElicitationPresenter
        };
        return connection;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Connects to all enabled servers from saved configurations.
    /// Failures are logged but do not prevent other servers from connecting.
    /// </summary>
    /// <param name="configs">Server configurations to connect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of error messages for failed connections, if any.</returns>
    public async Task<IReadOnlyList<string>> ConnectAllAsync(
        IEnumerable<McpServerConfig> configs,
        CancellationToken ct = default)
    {
        var configList = configs.Where(c => c.IsEnabled).ToList();
        LoggingService.LogInfo($"[MCP] ConnectAll: {configList.Count} enabled servers (parallel)");

        // Phase 1: Add all connections to registry (lock-protected, fast)
        var connections = new List<(McpServerConfig Config, McpServerConnection Conn)>();
        await _lock.WaitAsync(ct);
        try
        {
            foreach (var config in configList)
            {
                var connection = CreateConnection(config);
                _connections.Add(connection);
                connections.Add((config, connection));
            }
        }
        finally { _lock.Release(); }

        // Show "connecting" notification while servers start (first launch may be slow)
        var connectingToken = NotificationCenter.Instance.PostPersistent(
            InfoBarSeverity.Informational,
            string.Format(_loader.GetString("Mcp_ServersConnecting"), configList.Count),
            string.Empty);

        // Phase 2: Connect all in parallel (no lock — I/O bound)
        var tasks = connections.Select(async x =>
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                await x.Conn.ConnectAsync(cts.Token);
                return (x.Config.Name, Error: (string?)null);
            }
            catch (OperationCanceledException)
            {
                return (x.Config.Name, Error: "timeout (30s)");
            }
            catch (Exception ex)
            {
                return (x.Config.Name, Error: ex.Message);
            }
        });

        var results = await Task.WhenAll(tasks);
        ToolsChanged?.Invoke(this, EventArgs.Empty);

        NotificationCenter.Instance.Dismiss(connectingToken);

        var errors = results.Where(r => r.Error is not null).Select(r => $"{r.Name}: {r.Error}").ToList();

        var connected = _connections.Count(c => c.IsConnected);
        if (connected > 0)
        {
            NotificationCenter.Instance.Post(
                InfoBarSeverity.Success,
                string.Format(_loader.GetString("Mcp_ServersConnected"), connected),
                string.Empty);
        }

        if (errors.Count > 0)
        {
            NotificationCenter.Instance.Post(
                InfoBarSeverity.Warning,
                _loader.GetString("Mcp_ConnectionFailed"),
                string.Join(", ", errors.Select(e => e.Split(':')[0])),
                5000);
        }

        return errors;
    }

    /// <summary>
    /// Connects to an MCP server and adds it to the registry.
    /// </summary>
    /// <param name="config">Server configuration.</param>
    /// <param name="supportsRoots">
    /// When <see langword="true"/>, the connection declares MCP roots capability
    /// so the server can dynamically request folder updates without restarting.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<McpServerConnection> AddAndConnectAsync(
        McpServerConfig config,
        bool supportsRoots = false,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[MCP] AddAndConnect: {config.Name}");

        // Phase 1: Add to collection under lock (fast)
        McpServerConnection connection;
        await _lock.WaitAsync(ct);
        try
        {
            connection = CreateConnection(config, supportsRoots);
            _connections.Add(connection);
        }
        finally
        {
            _lock.Release();
        }

        // Phase 2: Connect outside lock (slow I/O)
        try
        {
            await connection.ConnectAsync(ct);
        }
        catch
        {
            // Connection stays in the list with Error state
            // so the user can see it and retry
        }

        ToolsChanged?.Invoke(this, EventArgs.Empty);
        return connection;
    }

    /// <summary>
    /// Adds a server to the registry without connecting.
    /// </summary>
    public McpServerConnection AddWithoutConnect(McpServerConfig config)
    {
        var connection = CreateConnection(config);
        _connections.Add(connection);
        return connection;
    }

    /// <summary>
    /// Disconnects and removes a server from the registry.
    /// </summary>
    public async Task RemoveAsync(McpServerConnection connection)
    {
        LoggingService.LogInfo($"[MCP] Removing: {connection.Config.Name}");
        await _lock.WaitAsync();
        try
        {
            await connection.DisposeAsync();
            _connections.Remove(connection);
            ToolsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reconnects a server (e.g., after config change or error).
    /// </summary>
    public async Task ReconnectAsync(
        McpServerConnection connection,
        CancellationToken ct = default)
    {
        LoggingService.LogInfo($"[MCP] Reconnecting: {connection.Config.Name}");
        await _lock.WaitAsync(ct);
        try
        {
            await connection.DisconnectAsync();
            await connection.ConnectAsync(ct);
            ToolsChanged?.Invoke(this, EventArgs.Empty);

            if (connection.IsConnected)
            {
                NotificationCenter.Instance.Post(
                    InfoBarSeverity.Success,
                    string.Format(_loader.GetString("Mcp_ServerReconnected"), connection.Config.Name),
                    string.Empty);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets tools grouped by server name for display.
    /// </summary>
    public Dictionary<string, IReadOnlyList<IAssistTool>> GetToolsByServer()
    {
        return _connections
            .Where(c => c.IsConnected && c.Config.IsEnabled)
            .ToDictionary(
                c => c.Config.Name,
                c => (IReadOnlyList<IAssistTool>)[.. c.Tools.Cast<IAssistTool>()]);
    }

    /// <summary>
    /// Connects or reconnects a built-in MCP server.
    /// If the server is disabled or has no folders, any existing connection is removed.
    /// The operation is atomic — the previous tool set is kept until the new connection
    /// is fully established, preventing a gap where no tools are available.
    /// </summary>
    /// <param name="serverKey">The built-in server key (e.g., "filesystem").</param>
    /// <param name="config">The server configuration.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ConnectBuiltInAsync(
        string serverKey,
        BuiltInServerConfig config,
        CancellationToken ct = default)
    {
        var builtInId = $"builtin_{serverKey}";
        LoggingService.LogInfo($"[MCP] ConnectBuiltIn: {serverKey}, enabled={config.IsEnabled}, folders={config.Folders.Count}");

        McpServerConnection? connection = null;

        // Phase 1: Collection manipulation under lock (fast)
        await _lock.WaitAsync(ct);
        try
        {
            // Skip if already connected (avoid killing a healthy connection during concurrent init)
            var existing = _connections.FirstOrDefault(c => c.Config.Id == builtInId);
            if (existing is not null && existing.IsConnected)
            {
                LoggingService.LogInfo($"[MCP] Built-in {serverKey} already connected, skipping");
                return;
            }

            // Remove failed or disconnected existing connection
            if (existing is not null)
            {
                await existing.DisposeAsync();
                _connections.Remove(existing);
            }

            // Create new McpServerConfig if valid
            var mcpConfig = BuiltInServerHelper.CreateMcpServerConfig(serverKey, config);
            if (mcpConfig is null)
            {
                // Disabled, no folders, or exe not found — just fire event for suppress fallback
                ToolsChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            connection = CreateConnection(mcpConfig);
            _connections.Add(connection);
        }
        finally
        {
            _lock.Release();
        }

        // Phase 2: Connect outside lock (slow I/O — allows parallelism)
        try
        {
            await connection.ConnectAsync(ct);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] Built-in server failed: {serverKey} — {ex.Message}");
            // Connection stays in error state, tools will be empty
        }

        ToolsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Updates a built-in server's configuration (e.g., when folders change).
    /// Alias for <see cref="ConnectBuiltInAsync"/> — handles disconnect + reconnect atomically.
    /// </summary>
    public Task UpdateBuiltInAsync(
        string serverKey,
        BuiltInServerConfig config,
        CancellationToken ct = default)
        => ConnectBuiltInAsync(serverKey, config, ct);

    /// <summary>
    /// Gets the connection for a built-in server, if any.
    /// </summary>
    public McpServerConnection? GetBuiltInConnection(string serverKey)
        => _connections.FirstOrDefault(c => c.Config.Id == $"builtin_{serverKey}");

    /// <summary>
    /// Kills all MCP server connections immediately without awaiting graceful shutdown.
    /// Intended for app exit only.
    /// </summary>
    public void ForceKillAll()
    {
        LoggingService.LogInfo($"[MCP] ForceKillAll: {_connections.Count} connections");
        foreach (var conn in _connections)
            conn.ForceKill();
        _connections.Clear();
    }

    /// <summary>
    /// Asynchronously kills all MCP server connections with timeout-guarded dispose.
    /// All connections are disposed in parallel (max 2 s total regardless of count).
    /// Intended for app exit only.
    /// </summary>
    public async ValueTask ForceKillAllAsync()
    {
        LoggingService.LogInfo($"[MCP] ForceKillAllAsync: {_connections.Count} connections");
        await Task.WhenAll(_connections.Select(c => c.ForceKillAsync().AsTask()));
        _connections.Clear();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        LoggingService.LogInfo($"[MCP] Disposing registry, {_connections.Count} connections");
        await _lock.WaitAsync();
        try
        {
            foreach (var conn in _connections)
                await conn.DisposeAsync();

            _connections.Clear();
        }
        finally
        {
            _lock.Release();
        }

        GC.SuppressFinalize(this);
    }

    #endregion
}
