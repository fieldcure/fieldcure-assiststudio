using FieldCure.AssistStudio.Models;

namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Answers the question "is this specific model reachable right now" for
/// any of the providers the app knows about (<c>ollama</c>, <c>openai</c>,
/// <c>anthropic</c>). Returns a plain <c>bool</c> — the UI is not asking
/// "why", only "can the user pick this and have it work".
/// </summary>
/// <remarks>
/// <para>
/// Not a gatekeeper. If the underlying probe fails for any reason (Ollama
/// daemon down, credential vault unreachable, network error), the
/// implementation returns <c>true</c> and the caller proceeds as if the
/// model were available. A false negative in this code path is worse than
/// a false positive — we would rather let the user hit a downstream error
/// than lock them out of a dialog over a transient infrastructure issue.
/// </para>
/// <para>
/// "Not installed" is a current-state fact, not a permanent verdict. The
/// user may install the model right after this call. Implementations must
/// cache at the instance level only — long-lived singletons would drift
/// from reality.
/// </para>
/// </remarks>
public interface IModelAvailabilityChecker
{
    /// <summary>
    /// Returns <c>true</c> if the specified model is reachable at the time
    /// of the call. Empty <paramref name="modelId"/> is always available
    /// (it represents the "disabled" contextualizer placeholder).
    /// </summary>
    Task<bool> IsAvailableAsync(string provider, string modelId, CancellationToken ct = default);

    /// <summary>
    /// Convenience wrapper that checks every configured model in a KB
    /// (embedding + contextualizer) and returns the problem list. Empty
    /// list means the KB's model configuration is currently usable.
    /// </summary>
    Task<IReadOnlyList<KbModelProblem>> CheckKbAsync(KnowledgeBase kb, CancellationToken ct = default);
}

/// <summary>Which slot in a KB's model configuration the problem relates to.</summary>
public enum KbModelRole
{
    Embedding,
    Contextualizer,
}

/// <summary>
/// A single unavailable model in a KB config. Carries only the facts the
/// UI needs to display: which slot, and which model id. No "why" — that
/// would be a guess at best (could be "not installed", could be "Ollama
/// not running", could be "network down") and framing a guess as a reason
/// pushes the user toward a specific mental model of what to fix.
/// </summary>
public sealed record KbModelProblem(KbModelRole Role, string ModelId);
