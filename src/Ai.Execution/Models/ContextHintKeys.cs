namespace FieldCure.Ai.Execution.Models;

/// <summary>
/// Well-known keys for <see cref="SubAgentRequest.ContextHints"/>.
/// Using constants prevents typos and enables IDE auto-completion.
/// Key names use lowercase snake_case for consistency with system prompt output.
/// </summary>
public static class ContextHintKeys
{
    /// <summary>
    /// Knowledge base ID for RAG <c>search_documents</c> / <c>get_document_chunk</c>.
    /// Value: KB UUID string (e.g., "3f8a1b2c-...").
    /// </summary>
    public const string KbId = "kb_id";

    /// <summary>
    /// Semicolon-separated workspace folder paths.
    /// Reserved for future use.
    /// </summary>
    public const string WorkspaceFolders = "workspace_folders";
}
