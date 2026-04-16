using System.Diagnostics;
using AssistStudio.Helpers;
using AssistStudio.Mcp.ModelAvailability;

namespace AssistStudio.Mcp;

/// <summary>
/// Manages RAG exec processes (headless indexing) and cancel file lifecycle.
/// </summary>
public static class RagProcessManager
{
    /// <summary>
    /// Validates the KB's configured models against the availability service
    /// and, if everything is reachable, launches the RAG exec process
    /// detached. Returns a structured result so the caller can surface a
    /// pre-flight failure in the UI instead of silently letting exec burn
    /// an OCR run on a broken configuration.
    /// </summary>
    /// <param name="kbId">Knowledge base ID.</param>
    /// <param name="force">If true, re-indexes all files regardless of hash.</param>
    public static async Task<StartExecResult> StartExecAsync(string kbId, bool force = false, string? partial = null)
    {
        // Load the KB config from the filesystem. ListAll is cheap enough
        // for pre-flight — the guards in there prevent backup folders from
        // masquerading as real KBs, which matters for the model lookup
        // below since a backup would otherwise answer "wrong" values.
        var kb = KnowledgeBaseStore.ListAll().FirstOrDefault(k => k.Id == kbId);
        if (kb is null)
        {
            LoggingService.LogError($"[RAG] exec aborted — KB not found: {kbId}");
            return StartExecResult.NotFound(kbId);
        }

        // Pre-flight: ask the availability checker whether the configured
        // models are currently reachable. If any model is down we bail
        // out with a per-slot state-only message instead of spawning
        // exec — the v1.4.2 2-commit pipeline would preserve OCR output
        // on failure, but the user's time is still wasted if Stage 3 or
        // Stage 4 blows up on every single chunk before the error
        // surfaces in the index timing log.
        var availabilityChecker = new ModelAvailabilityChecker();
        var problems = await availabilityChecker.CheckKbAsync(kb);
        if (problems.Count > 0)
        {
            LoggingService.LogWarning(
                $"[RAG] exec aborted — pre-flight found {problems.Count} unreachable model(s) in {kbId}: " +
                string.Join("; ", problems.Select(p => $"{p.Role} '{p.ModelId}'")));
            return StartExecResult.PreflightFailed(problems);
        }

        var exePath = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.RagKey);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            LoggingService.LogError($"[RAG] exec failed — executable not found: {exePath}");
            return StartExecResult.LaunchFailed("RAG executable not found. Run 'dotnet tool update FieldCure.Mcp.Rag'.");
        }

        var kbPath = KnowledgeBaseStore.GetKbPath(kbId);
        var args = $"exec --path \"{kbPath}\"";
        if (force) args += " --force";
        if (!string.IsNullOrEmpty(partial)) args += $" --partial {partial}";

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
            return StartExecResult.Success();
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[RAG] Failed to start exec: {ex.Message}");
            return StartExecResult.LaunchFailed(ex.Message);
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

/// <summary>
/// Outcome of a <see cref="RagProcessManager.StartExecAsync"/> call. Carries
/// enough information for the caller to render a ContentDialog or similar
/// surface without a second round-trip.
/// </summary>
public sealed record StartExecResult(
    StartExecOutcome Outcome,
    string? ErrorMessage,
    IReadOnlyList<KbModelProblem>? Problems)
{
    public bool IsSuccess => Outcome == StartExecOutcome.Success;

    public static StartExecResult Success() =>
        new(StartExecOutcome.Success, null, null);

    public static StartExecResult NotFound(string kbId) =>
        new(StartExecOutcome.KbNotFound, $"Knowledge base '{kbId}' not found.", null);

    public static StartExecResult PreflightFailed(IReadOnlyList<KbModelProblem> problems) =>
        new(StartExecOutcome.PreflightFailed, null, problems);

    public static StartExecResult LaunchFailed(string message) =>
        new(StartExecOutcome.LaunchFailed, message, null);
}

/// <summary>Discrete failure categories for <see cref="StartExecResult"/>.</summary>
public enum StartExecOutcome
{
    Success,
    KbNotFound,
    PreflightFailed,
    LaunchFailed,
}
