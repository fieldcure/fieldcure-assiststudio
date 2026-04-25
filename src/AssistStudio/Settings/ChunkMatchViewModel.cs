namespace AssistStudio.Settings;

/// <summary>
/// Presentation model for a single chunk hit displayed inside a KbCard.
/// </summary>
/// <remarks>
/// The score returned by mcp-rag is intentionally not surfaced. Raw
/// SQLite FTS5 BM25 values are tiny floats whose absolute magnitude
/// carries no user-meaningful signal — the server-side ordering is what
/// matters. RRF scores in hybrid mode are similarly compressed
/// (~0.016 range) and would also confuse rather than inform.
/// </remarks>
public sealed record ChunkMatchViewModel(
    string SourceName,
    string Snippet,
    string? ChunkId);
