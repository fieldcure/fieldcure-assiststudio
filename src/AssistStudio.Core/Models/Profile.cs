namespace FieldCure.AssistStudio.Models;

/// <summary>
/// A named profile that bundles a system prompt with optional preferred provider, model, and tools.
/// </summary>
public class Profile
{
    #region Properties

    /// <summary>Unique display name for this profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>The system prompt text.</summary>
    public string Text { get; set; } = "";

    /// <summary>Whether this is a built-in profile (cannot be deleted).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Preferred provider type to auto-select (e.g., "Ollama", "OpenAI").</summary>
    public string? PreferredProviderType { get; set; }

    /// <summary>Preferred model ID to auto-select (e.g., "llama3.1:latest").</summary>
    public string? PreferredModelId { get; set; }

    /// <summary>Tool names to enable when this profile is active.</summary>
    public List<string> ToolNames { get; set; } = [];

    /// <summary>
    /// When true, sends a single search_tools meta-tool instead of individual MCP tool definitions.
    /// </summary>
    public bool UseSearchTools { get; set; }

    /// <summary>
    /// Server IDs enabled for this profile (e.g., "builtin_filesystem", "github_abc123").
    /// When non-empty, only tools from these servers are discoverable via search_tools.
    /// Empty means no servers selected (built-in tools only).
    /// </summary>
    public List<string> EnabledServers { get; set; } = [];

    /// <summary>
    /// Default workspace folders for this profile.
    /// Used as the initial folder list when creating a new conversation with this profile.
    /// </summary>
    public List<string> WorkspaceFolders { get; set; } = [];

    #endregion
}
