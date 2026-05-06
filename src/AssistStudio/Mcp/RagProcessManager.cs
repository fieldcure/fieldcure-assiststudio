using System.Diagnostics;
using AssistStudio.Helpers;

#pragma warning disable CA1416 // PasswordVaultHelper uses Windows PasswordVault — AssistStudio is Windows-only

namespace AssistStudio.Mcp;

/// <summary>
/// Manages RAG orchestrator spawning and cancel file lifecycle.
/// Indexing requests go through the <c>start_reindex</c> MCP tool — this
/// class only handles the deferred-sweep orchestrator spawn (app shutdown).
/// <see cref="CancelExec"/> is still used to request graceful cancellation
/// of a running indexing process. Orphan KB folder cleanup is owned by RAG
/// serve itself (since v2.4.4) and is no longer spawned from the app.
/// </summary>
public static class RagProcessManager
{
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

    /// <summary>
    /// Spawns a detached orchestrator process that consumes the deferred
    /// queue sequentially. Returns once the child process is launched —
    /// the orchestrator continues running after AssistStudio exits.
    /// </summary>
    /// <param name="sweepAll">
    /// When true, passes <c>--sweep-all</c> so the orchestrator also
    /// processes <c>deferred=true</c> entries. Used at app shutdown.
    /// </param>
    public static void StartQueueOrchestrator(bool sweepAll = false)
    {
        var (command, prefixArgs) = BuiltInServerHelper.GetLaunchSpecForProcess(BuiltInServerHelper.RagKey);
        if (string.IsNullOrEmpty(command))
        {
            LoggingService.LogError("[RAG] exec-queue failed — no launch spec for RAG");
            return;
        }

        var psi = new ProcessStartInfo
        {
            FileName = command,
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        foreach (var a in prefixArgs) psi.ArgumentList.Add(a);
        psi.ArgumentList.Add("exec-queue");
        psi.ArgumentList.Add("--queue-file");
        psi.ArgumentList.Add(DeferredQueueStore.QueueFilePath);
        if (sweepAll) psi.ArgumentList.Add("--sweep-all");

        LoggingService.LogInfo($"[RAG] Starting exec-queue orchestrator: {command} {string.Join(' ', psi.ArgumentList)}");

        try
        {
            InjectApiKeys(psi);
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[RAG] Failed to start exec-queue: {ex.Message}");
        }
    }

    /// <summary>
    /// Injects API keys from AssistStudio's PasswordVault into the child process
    /// environment. The RAG process reads these via <c>Environment.GetEnvironmentVariable</c>
    /// instead of accessing PasswordVault directly, keeping it platform-agnostic.
    /// </summary>
    private static void InjectApiKeys(ProcessStartInfo psi)
    {
        (string presetName, string envVarName)[] mappings =
        [
            ("OpenAI", "OPENAI_API_KEY"),
            ("Claude", "ANTHROPIC_API_KEY"),
            ("Gemini", "GEMINI_API_KEY"),
            ("Voyage", "VOYAGE_API_KEY"),
            ("Groq", "GROQ_API_KEY"),
        ];

        foreach (var (preset, envVar) in mappings)
        {
            var key = PasswordVaultHelper.LoadApiKey(preset);
            if (!string.IsNullOrEmpty(key))
                psi.Environment[envVar] = key;
        }
    }
}
