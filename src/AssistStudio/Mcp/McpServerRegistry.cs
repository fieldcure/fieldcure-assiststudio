using System.Collections.ObjectModel;
using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;

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

    #endregion

    #region Properties

    /// <summary>Gets all connections (observable for UI binding).</summary>
    public ReadOnlyObservableCollection<McpServerConnection> Connections { get; }

    /// <summary>
    /// Gets all available MCP tools across all connected and enabled servers.
    /// </summary>
    public IReadOnlyList<McpToolAdapter> AllTools
        => _connections
            .Where(c => c.IsConnected && c.Config.IsEnabled)
            .SelectMany(c => c.Tools)
            .ToList();

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="McpServerRegistry"/> class.
    /// </summary>
    public McpServerRegistry()
    {
        Connections = new ReadOnlyObservableCollection<McpServerConnection>(_connections);
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
        var errors = new List<string>();
        var configList = configs.ToList();
        LoggingService.LogInfo($"[MCP] ConnectAll: {configList.Count(c => c.IsEnabled)} enabled servers");

        foreach (var config in configList.Where(c => c.IsEnabled))
        {
            try
            {
                await AddAndConnectAsync(config, ct: ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{config.Name}: {ex.Message}");
            }
        }

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
        await _lock.WaitAsync(ct);
        try
        {
            var connection = new McpServerConnection(config) { SupportsRoots = supportsRoots };
            _connections.Add(connection);

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
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Adds a server to the registry without connecting.
    /// </summary>
    public McpServerConnection AddWithoutConnect(McpServerConfig config)
    {
        var connection = new McpServerConnection(config);
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
                c => (IReadOnlyList<IAssistTool>)c.Tools.Cast<IAssistTool>().ToList());
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

        await _lock.WaitAsync(ct);
        try
        {
            // Find and remove existing built-in connection
            var existing = _connections.FirstOrDefault(c => c.Config.Id == builtInId);
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

            // Connect the server
            var connection = new McpServerConnection(mcpConfig);
            _connections.Add(connection);

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
        finally
        {
            _lock.Release();
        }
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
