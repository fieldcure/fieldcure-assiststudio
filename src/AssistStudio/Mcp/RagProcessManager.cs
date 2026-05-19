using AssistStudio.Helpers;
using Microsoft.Extensions.DependencyInjection;

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
        // Fire-and-forget: launch the queue orchestrator via IDnxHost on a background task
        // so we don't block the caller (typically app-shutdown path) on NuGet resolution.
        _ = Task.Run(StartQueueOrchestratorAsync);

        async Task StartQueueOrchestratorAsync()
        {
            try
            {
                var dnxHost = App.Services.GetRequiredService<IDnxHost>();

                var args = new List<string>
                {
                    "exec-queue",
                    "--queue-file",
                    DeferredQueueStore.QueueFilePath,
                };
                if (sweepAll) args.Add("--sweep-all");

                var env = LoadApiKeysEnv();

                LoggingService.LogInfo(
                    $"[RAG] Starting exec-queue orchestrator: FieldCure.Mcp.Rag@2.* {string.Join(' ', args)}");

                // Detached: we don't retain the Process. ToolHost returns it with stdio
                // redirected, but we don't need to read the streams — drain them onto null
                // sinks so the OS pipe buffers don't fill and block the child.
                var proc = await dnxHost.StartAsync(
                    packageId: "FieldCure.Mcp.Rag",
                    versionRange: "2.*",
                    args: args,
                    environmentVariables: env);

                proc.OutputDataReceived += static (_, _) => { };
                proc.ErrorDataReceived += static (_, e) =>
                {
                    if (e.Data is { Length: > 0 } line)
                        LoggingService.LogWarning($"[RAG:exec-queue] {line}");
                };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                // Close our handle to stdin so the child sees EOF if it ever tries to read.
                try { proc.StandardInput.Close(); } catch { }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[RAG] Failed to start exec-queue: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Loads provider API keys from <see cref="PasswordVaultHelper"/> into a child-process
    /// environment dictionary. The RAG process reads these via
    /// <c>Environment.GetEnvironmentVariable</c> instead of accessing PasswordVault directly,
    /// keeping it platform-agnostic.
    /// </summary>
    private static IReadOnlyDictionary<string, string?> LoadApiKeysEnv()
    {
        (string presetName, string envVarName)[] mappings =
        [
            ("OpenAI", "OPENAI_API_KEY"),
            ("Claude", "ANTHROPIC_API_KEY"),
            ("Gemini", "GEMINI_API_KEY"),
            ("Voyage", "VOYAGE_API_KEY"),
            ("Groq", "GROQ_API_KEY"),
        ];

        var env = new Dictionary<string, string?>(mappings.Length, StringComparer.Ordinal);
        foreach (var (preset, envVar) in mappings)
        {
            var key = PasswordVaultHelper.LoadApiKey(preset);
            if (!string.IsNullOrEmpty(key))
                env[envVar] = key;
        }
        return env;
    }
}
