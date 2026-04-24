using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// Thin wrapper around the RAG MCP server's per-KB tools
/// (<c>get_index_info</c>, <c>check_changes</c>). Returns parsed records
/// or <c>null</c> when the server is disconnected, letting callers fall
/// back to direct SQLite reads where possible.
/// </summary>
public static class KbMcpClient
{
    #region Public API

    /// <summary>
    /// Calls the <c>get_index_info</c> MCP tool on the RAG server for the
    /// given KB. Returns <c>null</c> when the RAG server is not connected
    /// (callers typically fall back to a direct SQLite read in that case).
    /// </summary>
    public static async Task<IndexInfoResult?> GetIndexInfoAsync(string kbId)
    {
        var connection = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected != true)
            return null;

        try
        {
            var argsJson = JsonSerializer.Serialize(new { kb_id = kbId });
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await connection.CallToolWithProgressAsync("get_index_info", args, null);

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            var totalFiles = root.GetProperty("total_files").GetInt32();
            var totalChunks = root.GetProperty("total_chunks").GetInt32();
            var isIndexing = root.GetProperty("is_indexing").GetBoolean();

            int? current = null, total = null;
            if (isIndexing && root.TryGetProperty("indexing_progress", out var prog) && prog.ValueKind == JsonValueKind.Object)
            {
                current = prog.GetProperty("current").GetInt32();
                total = prog.GetProperty("total").GetInt32();
            }

            var isPromptStale = root.TryGetProperty("is_prompt_stale", out var stale) && stale.GetBoolean();
            var lastIndexedAt = root.TryGetProperty("last_indexed_at", out var lai) ? lai.GetString() : null;
            var failedCount = root.TryGetProperty("last_failed_count", out var fc) ? fc.GetInt32() : 0;

            return new IndexInfoResult(totalFiles, totalChunks, isIndexing, current, total, isPromptStale, lastIndexedAt, failedCount);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Calls the <c>check_changes</c> MCP tool to compare filesystem
    /// against the index. Returns <c>null</c> when the RAG server is not
    /// connected.
    /// </summary>
    public static async Task<ChangeCheckResult?> CheckChangesAsync(string kbId)
    {
        var connection = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (connection?.IsConnected != true)
            return null;

        try
        {
            var argsJson = JsonSerializer.Serialize(new { kb_id = kbId });
            var args = JsonDocument.Parse(argsJson).RootElement;
            var result = await connection.CallToolWithProgressAsync("check_changes", args, null);

            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            return new ChangeCheckResult(
                root.GetProperty("added").GetInt32(),
                root.GetProperty("modified").GetInt32(),
                root.GetProperty("deleted").GetInt32(),
                root.TryGetProperty("failed", out var fail) ? fail.GetInt32() : 0,
                root.TryGetProperty("is_clean", out var clean) && clean.GetBoolean());
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

/// <summary>Parsed result from the <c>get_index_info</c> MCP tool.</summary>
public sealed record IndexInfoResult(
    int TotalFiles, int TotalChunks,
    bool IsIndexing, int? Current, int? Total,
    bool IsPromptStale, string? LastIndexedAt,
    int FailedCount);

/// <summary>Parsed result from the <c>check_changes</c> MCP tool.</summary>
public sealed record ChangeCheckResult(
    int Added, int Modified, int Deleted, int Failed, bool IsClean);
