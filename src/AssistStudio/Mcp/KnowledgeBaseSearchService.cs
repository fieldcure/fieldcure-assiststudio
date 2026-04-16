using System.Text.Json;
using AssistStudio.Settings;
using IOPath = System.IO.Path;

namespace AssistStudio.Mcp;

/// <summary>
/// Searches chunks in knowledge bases via the built-in RAG MCP server's
/// <c>search_documents</c> tool.
/// </summary>
internal sealed class KnowledgeBaseSearchService
{
    /// <summary>
    /// Searches chunks in the given knowledge base.
    /// Waits briefly for the RAG serve to become available if <paramref name="ragReady"/> is provided.
    /// Returns an empty list on failure or when the server is unavailable.
    /// </summary>
    public async Task<IReadOnlyList<ChunkMatchViewModel>> SearchAsync(
        string kbId, string query, int topK, Task? ragReady, CancellationToken ct)
    {
        if (ragReady is not null)
            await Task.WhenAny(ragReady, Task.Delay(3000, ct)).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();

        var conn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (conn is null || !conn.IsConnected)
            return [];

        var argsObj = new { kb_id = kbId, query, top_k = topK, search_mode = "bm25" };
        var args = JsonDocument.Parse(JsonSerializer.Serialize(argsObj)).RootElement;

        string resultJson;
        try
        {
            resultJson = await conn.CallToolWithProgressAsync(
                "search_documents", args, null, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }

        using var doc = JsonDocument.Parse(resultJson);
        if (!doc.RootElement.TryGetProperty("results", out var results))
            return [];

        var list = new List<ChunkMatchViewModel>();
        foreach (var item in results.EnumerateArray())
        {
            var sourcePath = item.TryGetProperty("source_path", out var sp)
                ? sp.GetString() ?? "" : "";
            var content = item.TryGetProperty("content", out var cn)
                ? cn.GetString() ?? "" : "";
            var chunkId = item.TryGetProperty("chunk_id", out var ci)
                ? ci.GetString() : null;
            var score = item.TryGetProperty("score", out var sc)
                ? sc.GetDouble() : 0.0;

            var displayName = IOPath.GetFileName(sourcePath);
            if (string.IsNullOrEmpty(displayName)) displayName = sourcePath;

            list.Add(new ChunkMatchViewModel(
                SourceName: displayName,
                Snippet: content.Length <= 200 ? content : content[..200] + "\u2026",
                Score: score,
                ChunkId: chunkId));
        }

        return list;
    }
}
