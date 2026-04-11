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
}
