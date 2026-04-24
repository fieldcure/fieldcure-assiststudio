namespace FieldCure.AssistStudio.Core.Models;

/// <summary>
/// Configuration for a built-in MCP server (e.g., Filesystem, RAG).
/// Serialized into .astx files and App Settings.
/// </summary>
/// <remarks>
/// Not sealed — future server types (e.g., RAG) may subclass to add
/// extended settings such as embedding model or chunk size.
/// </remarks>
public class BuiltInServerConfig
{
    /// <summary>
    /// Gets or sets whether this server is enabled.
    /// Default is <see langword="false"/>; users must add folders and
    /// enable the server explicitly.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the folder paths for this server.
    /// Filesystem: allowed directories for file operations.
    /// RAG: document folder to index and search (single entry).
    /// </summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>
    /// Gets or sets environment variable key names for this server.
    /// Values are stored in PasswordVault under <c>McpEnv_builtin_{serverKey}_{key}</c>.
    /// Used by the RAG server for embedding configuration
    /// (EMBEDDING_BASE_URL, EMBEDDING_API_KEY, EMBEDDING_MODEL, EMBEDDING_DIMENSION).
    /// </summary>
    public List<string>? EnvironmentVariableKeys { get; set; }

    /// <summary>
    /// Gets or sets the search engine name for the Essentials server.
    /// Passed as <c>--search-engine</c> CLI argument.
    /// Values: <c>null</c> or <c>"default"</c> for Bing/DuckDuckGo fallback,
    /// or <c>"serper"</c>, <c>"tavily"</c>, <c>"serpapi"</c> for paid engines.
    /// </summary>
    public string? SearchEngine { get; set; }
}
