using System.Text.Json.Serialization;

namespace FieldCure.AssistStudio.Models;

/// <summary>
/// MCP transport type.
/// </summary>
public enum McpTransportType
{
    /// <summary>Standard I/O transport (child process).</summary>
    Stdio,

    /// <summary>HTTP transport (SSE or Streamable HTTP).</summary>
    Http
}

/// <summary>
/// Configuration for an MCP server connection.
/// Persisted to mcp_servers.json. Environment variable values are stored
/// separately in PasswordVault for security.
/// </summary>
public class McpServerConfig
{
    #region Properties

    /// <summary>Short unique identifier for this server.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Display name of the MCP server.</summary>
    public string Name { get; set; } = "";

    /// <summary>Transport type: Stdio or Http.</summary>
    public McpTransportType TransportType { get; set; } = McpTransportType.Stdio;

    /// <summary>
    /// Command to launch the server (Stdio only).
    /// Example: "npx", "python", "dotnet"
    /// </summary>
    public string? Command { get; set; }

    /// <summary>
    /// Arguments for the command (Stdio only).
    /// Example: ["-y", "@modelcontextprotocol/server-github"]
    /// </summary>
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Server URL (Http only).
    /// Example: "http://localhost:3001/mcp"
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Environment variable values for the server process (Stdio only).
    /// Not serialized — values are stored in PasswordVault.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string>? EnvironmentVariables { get; set; }

    /// <summary>
    /// Environment variable key names (serialized to JSON).
    /// The actual values are stored in PasswordVault under
    /// <c>McpEnv_{Id}_{key}</c>.
    /// </summary>
    public List<string>? EnvironmentVariableKeys { get; set; }

    /// <summary>
    /// Short description of what this server provides.
    /// Injected into search_tools description for AI model awareness.
    /// Auto-populated from server's self-reported info on first connect;
    /// user can override in settings.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>Whether this server is enabled.</summary>
    public bool IsEnabled { get; set; } = true;

    #endregion
}
