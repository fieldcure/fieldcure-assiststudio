using System.Diagnostics;
using AssistStudio.Helpers;

namespace AssistStudio.Mcp;

/// <summary>
/// Manages RAG exec processes (headless indexing) and cancel file lifecycle.
/// </summary>
public static class RagProcessManager
{
    /// <summary>
    /// Starts the RAG exec process for a knowledge base.
    /// The process runs detached and completes independently of the app.
    /// </summary>
    /// <param name="kbId">Knowledge base ID.</param>
    /// <param name="force">If true, re-indexes all files regardless of hash.</param>
    public static void StartExec(string kbId, bool force = false)
    {
        var kbPath = KnowledgeBaseStore.GetKbPath(kbId);
        var exePath = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.RagKey);

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            LoggingService.LogError($"[RAG] exec failed — executable not found: {exePath}");
            return;
        }

        var args = $"exec --path \"{kbPath}\"";
        if (force) args += " --force";

        LoggingService.LogInfo($"[RAG] Starting exec: {exePath} {args}");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[RAG] Failed to start exec: {ex.Message}");
        }
    }

    /// <summary>
    /// Requests graceful cancellation of the exec process by creating a cancel file.
    /// The exec process checks for this file between chunks and exits with code 2.
    /// </summary>
    public static void CancelExec(string kbId)
    {
        var cancelPath = Path.Combine(KnowledgeBaseStore.GetKbPath(kbId), "cancel");

        try
        {
            File.WriteAllText(cancelPath, "");
            LoggingService.LogInfo($"[RAG] Cancel file created for KB {kbId}");
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[RAG] Failed to create cancel file: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns true if exec is currently running for the given KB.
    /// Checks the <c>_indexing_lock</c> table in rag.db.
    /// </summary>
    public static bool IsExecRunning(string kbId) =>
        KnowledgeBaseStore.GetIndexingStatus(kbId) is not null;

    /// <summary>
    /// Returns indexing progress, or null if not indexing.
    /// </summary>
    public static KbIndexingStatus? GetProgress(string kbId) =>
        KnowledgeBaseStore.GetIndexingStatus(kbId);
}
