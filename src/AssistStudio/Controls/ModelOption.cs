namespace AssistStudio.Controls;

/// <summary>
/// Represents a single embedding or contextualizer model entry
/// shown in the ComboBox dropdowns of the KB creation/settings dialogs.
/// </summary>
public sealed class ModelOption
{
    /// <summary>Model identifier (e.g. "nomic-embed-text").</summary>
    public required string Id { get; init; }

    /// <summary>Display-cased provider name (e.g. "Ollama", "OpenAI", "Claude").</summary>
    public required string Provider { get; init; }

    /// <summary>Human-readable model name.</summary>
    public required string Label { get; init; }

    /// <summary>Metadata line (e.g. "768d · 274MB · multilingual").</summary>
    public required string Meta { get; init; }

    /// <summary>Whether the model was reachable at probe time.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>
    /// Localized status text shown to the right of the model name.
    /// Empty string when available; "(not installed)" when not.
    /// </summary>
    public string StatusText { get; set; } = "";

    /// <summary>Whether this is a local model (Ollama) that may benefit from deferred indexing.</summary>
    public bool IsLocal { get; init; }
}
