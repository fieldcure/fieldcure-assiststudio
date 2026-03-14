namespace FieldCure.AssistStudio.Models;

/// <summary>
/// A named system prompt preset that can be selected per conversation.
/// </summary>
public class PromptPreset
{
    /// <summary>Unique display name for this preset.</summary>
    public string Name { get; set; } = "";

    /// <summary>The system prompt text.</summary>
    public string Text { get; set; } = "";

    /// <summary>Whether this is a built-in preset (cannot be deleted).</summary>
    public bool IsBuiltIn { get; set; }
}
