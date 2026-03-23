using FieldCure.AssistStudio.Models;

namespace AssistStudio.Mcp;

/// <summary>
/// Static helper for built-in MCP server configuration resolution and
/// <see cref="McpServerConfig"/> construction.
/// </summary>
public static class BuiltInServerHelper
{
    #region Constants

    /// <summary>Config dictionary key for the Filesystem server.</summary>
    public const string FilesystemKey = "filesystem";

    /// <summary>Config dictionary key for the RAG server.</summary>
    public const string RagKey = "rag";

    /// <summary>Display name for the Filesystem server.</summary>
    public const string FilesystemDisplayName = "Workspace Folders";

    /// <summary>Display name for the RAG server.</summary>
    public const string RagDisplayName = "Knowledge Folders";

    /// <summary>
    /// Tool names from the built-in Filesystem MCP server that are read-only
    /// and do not require user confirmation.
    /// </summary>
    private static readonly HashSet<string> ReadOnlyToolNames =
    [
        "read_file", "read_multiple_files", "read_file_lines",
        "list_directory", "directory_tree",
        "search_files", "search_within_files", "get_file_info",
    ];

    /// <summary>
    /// Built-in tool names that are suppressed when the Filesystem MCP server is active.
    /// These built-in tools overlap with MCP Filesystem tools.
    /// </summary>
    public static readonly HashSet<string> SuppressedBuiltInToolNames =
    [
        "read_file", "write_file", "search_files",
    ];

    /// <summary>
    /// Maps server keys to their executable names.
    /// </summary>
    private static readonly Dictionary<string, (string ExeName, string DisplayName)> ServerDefinitions = new()
    {
        [FilesystemKey] = ("fieldcure-mcp-filesystem", FilesystemDisplayName),
        [RagKey] = ("fieldcure-mcp-rag", RagDisplayName),
    };

    #endregion

    #region Methods

    /// <summary>
    /// Returns the default built-in server configurations.
    /// All servers start disabled with no folders.
    /// </summary>
    public static Dictionary<string, BuiltInServerConfig> GetDefaults() => new()
    {
        [FilesystemKey] = new BuiltInServerConfig { IsEnabled = false, Folders = [] },
        [RagKey] = new BuiltInServerConfig { IsEnabled = false, Folders = [] },
    };

    /// <summary>
    /// Merges App Settings defaults with optional per-conversation overrides.
    /// If the conversation has overrides, they take precedence entirely.
    /// </summary>
    public static Dictionary<string, BuiltInServerConfig> ResolveConfigs(
        Dictionary<string, BuiltInServerConfig> appDefaults,
        Dictionary<string, BuiltInServerConfig>? conversationConfigs = null)
    {
        if (conversationConfigs is { Count: > 0 })
            return new(conversationConfigs);

        return new(appDefaults);
    }

    /// <summary>
    /// Creates a <see cref="McpServerConfig"/> for a built-in server.
    /// Returns <see langword="null"/> if the server is disabled, has no folders,
    /// or the executable is not found.
    /// </summary>
    public static McpServerConfig? CreateMcpServerConfig(string serverKey, BuiltInServerConfig config)
    {
        if (!config.IsEnabled || config.Folders.Count == 0)
            return null;

        if (!ServerDefinitions.TryGetValue(serverKey, out var def))
            return null;

        var exePath = GetServerExePath(serverKey);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return null;

        return new McpServerConfig
        {
            Id = $"builtin_{serverKey}",
            Name = def.DisplayName,
            TransportType = McpTransportType.Stdio,
            Command = exePath,
            Arguments = [.. config.Folders],
            IsEnabled = true,
            IsBuiltIn = true,
            Description = serverKey switch
            {
                FilesystemKey => "Secure filesystem operations within allowed directories.",
                RagKey => "Index and search local documents.",
                _ => "",
            },
        };
    }

    /// <summary>
    /// Resolves the path to a bundled server executable.
    /// Looks in the app's base directory under a <c>servers/</c> subfolder.
    /// </summary>
    public static string GetServerExePath(string serverKey)
    {
        if (!ServerDefinitions.TryGetValue(serverKey, out var def))
            return "";

        var appDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? $"{def.ExeName}.exe" : def.ExeName;
        return Path.Combine(appDir, "servers", exeName);
    }

    /// <summary>
    /// Determines whether a tool from a built-in server requires user confirmation.
    /// Read-only tools (list, read, search, get_info) do not require confirmation.
    /// Write/modify/delete tools require confirmation.
    /// </summary>
    public static bool? GetRequiresConfirmation(string toolName)
    {
        return ReadOnlyToolNames.Contains(toolName) ? false : true;
    }

    /// <summary>
    /// Gets the display name for a built-in server key.
    /// </summary>
    public static string GetDisplayName(string serverKey)
    {
        return ServerDefinitions.TryGetValue(serverKey, out var def)
            ? def.DisplayName
            : serverKey;
    }

    #endregion
}
