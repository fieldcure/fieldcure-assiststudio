namespace FieldCure.AssistStudio.Models;

/// <summary>
/// A named profile that bundles a system prompt with optional preferred provider, model, and tools.
/// Serialized to JSON via <c>AppJsonContext</c>.
/// </summary>
public class Profile
{
    #region Events

    /// <summary>
    /// Raised when any tool-related property (<see cref="ToolNames"/>, <see cref="EnabledServers"/>,
    /// <see cref="UseSearchTools"/>) changes. Subscribers should refresh their tool resolution.
    /// </summary>
    public event EventHandler? ToolSettingsChanged;

    #endregion

    #region Properties

    /// <summary>Unique display name for this profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>The system prompt text for this profile.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("Text")]
    public string SystemPrompt { get; set; } = "";

    /// <summary>Whether this is a built-in profile (cannot be deleted).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>Preferred provider type to auto-select (e.g., "Ollama", "OpenAI").</summary>
    public string? PreferredProviderType { get; set; }

    /// <summary>Preferred model ID to auto-select (e.g., "llama3.1:latest").</summary>
    public string? PreferredModelId { get; set; }

    /// <summary>Tool names to enable when this profile is active.</summary>
    public List<string> ToolNames
    {
        get => _toolNames;
        set
        {
            _toolNames = value;
            ToolSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// When true, sends a single search_tools meta-tool instead of individual MCP tool definitions.
    /// </summary>
    public bool UseSearchTools
    {
        get => _useSearchTools;
        set
        {
            _useSearchTools = value;
            ToolSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Server IDs enabled for this profile (e.g., "builtin_filesystem", "github_abc123").
    /// When non-empty, only tools from these servers are discoverable via search_tools.
    /// Empty means no servers selected (built-in tools only).
    /// </summary>
    public List<string> EnabledServers
    {
        get => _enabledServers;
        set
        {
            _enabledServers = value;
            ToolSettingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region Fields

    private List<string> _toolNames = [];
    private bool _useSearchTools;
    private List<string> _enabledServers = [];

    #endregion
}
