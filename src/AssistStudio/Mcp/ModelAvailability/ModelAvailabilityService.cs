using FieldCure.AssistStudio.Models;

namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Orchestrator that dispatches per-model availability checks to the
/// appropriate <see cref="IModelAvailabilityChecker"/> based on provider
/// name. Holds a small registry of checker instances so the underlying
/// probes (Ollama <c>/api/tags</c>, credential vault lookups) are cached
/// across checks within a single service lifetime.
/// </summary>
/// <remarks>
/// Callers should create one service per logical operation (opening a
/// dialog, refreshing the KB list, pre-flighting an exec) and dispose of
/// it when done. Do not share across long-lived scopes — the cache would
/// go stale if the user installed a new Ollama model or pasted a new API
/// key in the meantime.
/// </remarks>
public sealed class ModelAvailabilityService
{
    private readonly Dictionary<string, IModelAvailabilityChecker> _checkers;

    public ModelAvailabilityService()
    {
        _checkers = new Dictionary<string, IModelAvailabilityChecker>(StringComparer.OrdinalIgnoreCase)
        {
            [new OllamaChecker().ProviderName] = new OllamaChecker(),
            [new OpenAiChecker().ProviderName] = new OpenAiChecker(),
            [new AnthropicChecker().ProviderName] = new AnthropicChecker(),
        };
    }

    /// <summary>
    /// Returns <c>null</c> if the specified model is currently usable, or
    /// a short user-visible reason string if it is not. Unknown providers
    /// are treated as available — we would rather miss a false positive
    /// than block a legitimate custom provider.
    /// </summary>
    public async Task<string?> CheckModelAsync(
        string provider, string modelId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(modelId))
            return null;
        if (!_checkers.TryGetValue(provider, out var checker))
            return null;
        return await checker.CheckModelAsync(modelId, ct);
    }

    /// <summary>
    /// Checks every configured model in a KB (embedding + contextualizer)
    /// and returns a list of problem descriptions. Empty list means the
    /// KB's model configuration is fully usable.
    /// </summary>
    public async Task<IReadOnlyList<KbModelProblem>> CheckKbAsync(
        KnowledgeBase kb, CancellationToken ct = default)
    {
        var problems = new List<KbModelProblem>();

        var embedReason = await CheckModelAsync(kb.Embedding.Provider, kb.Embedding.Model, ct);
        if (embedReason is not null)
            problems.Add(new KbModelProblem(
                KbModelRole.Embedding, kb.Embedding.Provider, kb.Embedding.Model, embedReason));

        if (!string.IsNullOrEmpty(kb.Contextualizer.Model))
        {
            var ctxReason = await CheckModelAsync(kb.Contextualizer.Provider, kb.Contextualizer.Model, ct);
            if (ctxReason is not null)
                problems.Add(new KbModelProblem(
                    KbModelRole.Contextualizer, kb.Contextualizer.Provider, kb.Contextualizer.Model, ctxReason));
        }

        return problems;
    }
}

/// <summary>
/// Which slot in a KB's model configuration the problem relates to.
/// </summary>
public enum KbModelRole
{
    Embedding,
    Contextualizer,
}

/// <summary>
/// A single unavailable model in a KB config, paired with a short reason
/// the UI can display without further formatting.
/// </summary>
public sealed record KbModelProblem(
    KbModelRole Role,
    string Provider,
    string ModelId,
    string Reason);
