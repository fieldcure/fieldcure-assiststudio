namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Metadata for assistant messages that summarize prior conversation turns
/// for token management purposes. Used during prompt building to replace
/// the covered original messages with this compact summary.
/// </summary>
public class SummaryMeta
{
    /// <summary>
    /// Schema version for forward compatibility. Increment when adding
    /// fields that change summary semantics.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// IDs of messages covered (replaced) by this summary, in chronological order.
    /// Used for validation and debugging. Not strictly required for prompt building —
    /// the builder locates the most recent summary in the active path and truncates
    /// everything before it.
    /// </summary>
    public IReadOnlyList<string> CoveredMessageIds { get; init; } = [];

    /// <summary>
    /// Approximate token count of covered content (from the summarization request's input tokens).
    /// Used with the summary message's <see cref="ChatMessage.TokenCount"/> (output tokens)
    /// to display compression ratio in the UI. Zero when unavailable.
    /// </summary>
    public int CoveredTokenCount { get; set; }
}
