namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Configuration for a built-in MCP server (e.g., Filesystem, RAG).
/// Serialized into .astd files and App Settings.
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
    /// RAG: document folders to index and search.
    /// </summary>
    public List<string> Folders { get; set; } = [];
}
