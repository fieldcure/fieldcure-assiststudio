using System.Collections.ObjectModel;
using FieldCure.AssistStudio.Models;

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

        foreach (var config in configs.Where(c => c.IsEnabled))
        {
            try
            {
                await AddAndConnectAsync(config, ct);
            }
            catch (Exception ex)
            {
                errors.Add($"{config.Name}: {ex.Message}");
            }
        }

        return errors;
    }

    /// <summary>
    /// Connects to an MCP server and adds it to the registry.
    /// </summary>
    public async Task<McpServerConnection> AddAndConnectAsync(
        McpServerConfig config,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var connection = new McpServerConnection(config);
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
        await _lock.WaitAsync(ct);
        try
        {
            await connection.DisconnectAsync();
            await connection.ConnectAsync(ct);
            ToolsChanged?.Invoke(this, EventArgs.Empty);
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
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
