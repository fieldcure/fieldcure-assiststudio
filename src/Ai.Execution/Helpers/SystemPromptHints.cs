using System.Text;
using FieldCure.Ai.Execution.Models;

namespace FieldCure.Ai.Execution.Helpers;

/// <summary>
/// Builds system prompt fragments from <see cref="SubAgentRequest.ContextHints"/>.
/// Each well-known key maps to a structured prompt section.
/// </summary>
public static class SystemPromptHints
{
    /// <summary>
    /// Builds a RAG knowledge base hint for the system prompt.
    /// Identical wording to ChatPanel's kb_id injection.
    /// </summary>
    public static string BuildRagHint(string kbId) =>
        $"""

        ## Knowledge Base
        Use `search_documents` to find relevant information before answering.
        Always pass kb_id="{kbId}" when calling search_documents or get_document_chunk.
        If initial search returns no results, retry with a lower threshold (e.g., 0.1) or different query terms.
        """;

    /// <summary>
    /// Converts a <see cref="SubAgentRequest.ContextHints"/> dictionary into
    /// system prompt text. Returns null if no hints produce output.
    /// </summary>
    public static string? BuildFromHints(IReadOnlyDictionary<string, string>? hints)
    {
        if (hints is null or { Count: 0 })
            return null;

        var sb = new StringBuilder();

        if (hints.TryGetValue(ContextHintKeys.KbId, out var kbId)
            && !string.IsNullOrEmpty(kbId))
        {
            sb.Append(BuildRagHint(kbId));
        }

        // Future: workspace_folders, etc.

        return sb.Length > 0 ? sb.ToString() : null;
    }
}
