using System.Text.Json.Serialization;

namespace FieldCure.AssistStudio.Core.Models;

/// <summary>
/// A named profile that bundles a system prompt with an optional preferred model and tool selection.
/// Serialized to JSON via <c>AppJsonContext</c>.
/// </summary>
public class Profile
{
    #region Events

    /// <summary>
    /// Raised when any tool-related property (<see cref="ToolNames"/>, <see cref="EnabledServers"/>,
    /// <see cref="UseSearchTools"/>) changes.
    /// </summary>
    public event EventHandler? ToolSettingsChanged;

    #endregion

    #region Properties

    /// <summary>Unique display name for this profile.</summary>
    public string Name { get; set; } = "";

    /// <summary>The system prompt text for this profile.</summary>
    [JsonPropertyName("Text")]
    public string SystemPrompt { get; set; } = "";

    /// <summary>Whether this is a built-in profile (cannot be deleted).</summary>
    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// Preferred <c>ProviderModel.Name</c> to auto-select for new tabs using this profile.
    /// Replaces the legacy (<c>PreferredProviderType</c>, <c>PreferredModelId</c>) pair.
    /// </summary>
    public string? PreferredModelName { get; set; }

    /// <summary>
    /// Legacy field (pre-1.0). Populated when reading old profile JSON; consumed once by
    /// <c>AppSettings.LoadProfiles</c> migration to backfill <see cref="PreferredModelName"/>.
    /// Skipped on write because it ends up null after migration and the global JSON
    /// source-gen context ignores nulls.
    /// </summary>
    [JsonPropertyName("PreferredProviderType")]
    public string? LegacyPreferredProviderType { get; set; }

    /// <summary>
    /// Legacy field (pre-1.0). Populated when reading old profile JSON. Not consumed
    /// directly — migration uses <see cref="LegacyPreferredProviderType"/> to look up
    /// the matching <c>ProviderModel.Name</c>. Skipped on write once cleared.
    /// </summary>
    [JsonPropertyName("PreferredModelId")]
    public string? LegacyPreferredModelId { get; set; }

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
    /// Server IDs enabled for this profile. When non-empty, only tools from these servers
    /// are discoverable via search_tools. Empty means built-in tools only.
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
