namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Provider source mode for an auxiliary task (title, summary, sub-agent).
/// </summary>
public enum AuxiliaryTaskSource
{
    /// <summary>Inherit the parent conversation's provider.</summary>
    Inherit,

    /// <summary>Use an explicitly specified preset.</summary>
    Specific
}
