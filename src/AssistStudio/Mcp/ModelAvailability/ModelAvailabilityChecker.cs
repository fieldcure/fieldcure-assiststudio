using AssistStudio.Helpers;
using FieldCure.Ai.Providers;
using FieldCure.AssistStudio.Models;

namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Single-class implementation of <see cref="IModelAvailabilityChecker"/>
/// that covers Ollama (local <c>/api/tags</c> probe) and the two cloud
/// providers (OpenAI + Anthropic credential vault lookups). Per-provider
/// logic is kept as private methods rather than separate classes because
/// the surface area is tiny and the callers only ever need the dispatcher.
/// </summary>
/// <remarks>
/// <para>
/// Caching is per-instance, not per-process. Create one instance per
/// logical operation (dialog open, KB list refresh, pre-flight check)
/// and dispose when done. The Ollama tag probe is wrapped in a shared
/// <see cref="Task"/> so multiple <see cref="IsAvailableAsync"/> calls
/// against different Ollama models on the same instance only hit
/// <c>/api/tags</c> once.
/// </para>
/// <para>
/// On any probe failure — daemon down, network error, credential vault
/// unreachable — every check on this instance returns <c>true</c>. This
/// is intentional: the point is to help the user notice a configuration
/// gap, not to block them on our own diagnostic flakes.
/// </para>
/// </remarks>
public sealed class ModelAvailabilityChecker : IModelAvailabilityChecker
{
    private readonly string _ollamaBaseUrl;
    private Task<IReadOnlySet<string>?>? _ollamaTagsCached;
    private readonly object _ollamaGate = new();
    private bool? _hasOpenAiKey;
    private bool? _hasClaudeKey;

    public ModelAvailabilityChecker(string ollamaBaseUrl = "http://localhost:11434")
    {
        _ollamaBaseUrl = ollamaBaseUrl;
    }

    public async Task<bool> IsAvailableAsync(
        string provider, string modelId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(modelId))
            return true;

        try
        {
            return provider?.ToLowerInvariant() switch
            {
                "ollama" => await IsOllamaInstalledAsync(modelId, ct),
                "openai" => HasOpenAiKey(),
                "anthropic" => HasClaudeKey(),
                _ => true, // unknown provider → permissive
            };
        }
        catch
        {
            // Any failure in the probe is swallowed. See the class-level
            // remarks for the rationale.
            return true;
        }
    }

    public async Task<IReadOnlyList<KbModelProblem>> CheckKbAsync(
        KnowledgeBase kb, CancellationToken ct = default)
    {
        var problems = new List<KbModelProblem>();

        if (!await IsAvailableAsync(kb.Embedding.Provider, kb.Embedding.Model, ct))
            problems.Add(new KbModelProblem(KbModelRole.Embedding, kb.Embedding.Model));

        if (!string.IsNullOrEmpty(kb.Contextualizer.Model)
            && !await IsAvailableAsync(kb.Contextualizer.Provider, kb.Contextualizer.Model, ct))
            problems.Add(new KbModelProblem(KbModelRole.Contextualizer, kb.Contextualizer.Model));

        return problems;
    }

    #region Ollama probe

    private async Task<bool> IsOllamaInstalledAsync(string modelId, CancellationToken ct)
    {
        Task<IReadOnlySet<string>?> fetchTask;
        lock (_ollamaGate)
        {
            _ollamaTagsCached ??= FetchOllamaTagsAsync(ct);
            fetchTask = _ollamaTagsCached;
        }

        var tags = await fetchTask;
        if (tags is null)
            return true; // probe failed → permissive

        if (tags.Contains(modelId))
            return true;

        // Some Ollama tags carry a quant suffix like
        // "qwen3-embedding:8b-q4_K_M" while the catalog id is just
        // "qwen3-embedding:8b". Accept either direction.
        var colon = modelId.IndexOf(':');
        var bare = colon >= 0 ? modelId[..colon] : modelId;
        return tags.Contains(bare);
    }

    private async Task<IReadOnlySet<string>?> FetchOllamaTagsAsync(CancellationToken ct)
    {
        try
        {
            using var manager = new OllamaModelManager(_ollamaBaseUrl);
            var models = await manager.ListLocalModelsAsync(ct);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in models)
            {
                set.Add(m.Id);
                var colon = m.Id.IndexOf(':');
                if (colon >= 0)
                    set.Add(m.Id[..colon]);
            }
            return set;
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Cloud key probes

    private bool HasOpenAiKey()
    {
        _hasOpenAiKey ??= SafeHasApiKey("OpenAI");
        return _hasOpenAiKey.Value;
    }

    private bool HasClaudeKey()
    {
        _hasClaudeKey ??= SafeHasApiKey("Claude");
        return _hasClaudeKey.Value;
    }

    private static bool SafeHasApiKey(string preset)
    {
        try { return PasswordVaultHelper.HasApiKey(preset); }
        catch { return false; }
    }

    #endregion
}
