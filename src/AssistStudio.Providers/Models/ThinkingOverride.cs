namespace FieldCure.AssistStudio.Models;

/// <summary>
/// User override for thinking support detection.
/// </summary>
public enum ThinkingOverride
{
    /// <summary>Use provider's heuristic to determine thinking support.</summary>
    Auto,

    /// <summary>Always treat as thinking-capable, regardless of provider heuristic.</summary>
    ForceOn,

    /// <summary>Always treat as non-thinking, regardless of provider heuristic.</summary>
    ForceOff
}
