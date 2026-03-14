namespace FieldCure.AssistStudio.Models;

/// <summary>
/// A single chunk of retrieved context from a knowledge source.
/// </summary>
/// <param name="Text">The retrieved text content.</param>
/// <param name="Source">An optional source identifier (e.g., file name, URL, document title).</param>
/// <param name="Score">Relevance score from the retrieval system (higher is more relevant).</param>
public record ContextChunk(string Text, string? Source = null, double Score = 0);

/// <summary>
/// Retrieves relevant domain knowledge for a given user query (RAG pattern).
/// </summary>
/// <remarks>
/// Implement this interface to add retrieval-augmented generation support.
/// Retrieved chunks are injected into the system prompt alongside workspace context.
/// This is optional — if not provided, the pipeline skips the retrieval step.
/// </remarks>
public interface IContextProvider
{
    /// <summary>
    /// Searches for context chunks relevant to the user's query.
    /// </summary>
    /// <returns>A list of relevant chunks ordered by relevance, or empty if none found.</returns>
    Task<IReadOnlyList<ContextChunk>> RetrieveAsync(
        string query,
        int maxResults = 5,
        CancellationToken ct = default);
}
