namespace FieldCure.AssistStudio.Core.Models;

/// <summary>
/// Tracks per-conversation tool overrides.
/// Initialized from the active <see cref="Profile"/>'s defaults,
/// then modified via the tools button in the input container.
/// </summary>
public class ConversationToolState
{
    #region Fields

    private readonly HashSet<string> _enabledToolNames;
    private readonly HashSet<string> _profileDefaults;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes from a profile's default tool selection.
    /// Called when a conversation starts or the profile changes.
    /// </summary>
    /// <param name="profile">The active profile to initialize from.</param>
    public ConversationToolState(Profile profile)
    {
        _profileDefaults = [.. profile.ToolNames];
        _enabledToolNames = [.. profile.ToolNames];
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether this state differs from the profile defaults.
    /// When <see langword="true"/>, the tools button should show a badge indicator.
    /// </summary>
    public bool HasOverrides { get; private set; }

    /// <summary>
    /// Gets the names of currently enabled tools.
    /// </summary>
    public IReadOnlySet<string> EnabledToolNames => _enabledToolNames;

    #endregion

    #region Methods

    /// <summary>
    /// Toggles a tool on or off for this conversation.
    /// </summary>
    public void Toggle(string toolName)
    {
        if (!_enabledToolNames.Remove(toolName))
            _enabledToolNames.Add(toolName);

        HasOverrides = !_enabledToolNames.SetEquals(_profileDefaults);
    }

    /// <summary>
    /// Resets to profile defaults.
    /// </summary>
    public void ResetToDefaults(Profile profile)
    {
        _enabledToolNames.Clear();
        _enabledToolNames.UnionWith(profile.ToolNames);

        _profileDefaults.Clear();
        _profileDefaults.UnionWith(profile.ToolNames);

        HasOverrides = false;
    }

    #endregion
}
