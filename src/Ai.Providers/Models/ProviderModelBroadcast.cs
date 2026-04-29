using System.Collections.Generic;

namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Implements the per-Provider "broadcast" invariant for <see cref="ProviderModel"/>
/// collections: when multiple <see cref="ProviderModel"/> instances share a
/// <see cref="ProviderModel.ProviderType"/>, certain fields must be identical
/// across all of them (max tokens, temperature, streaming, PDF handling, thinking
/// configuration, base URL). Per-model fields (KeepAlive, NumCtx) are preserved per
/// instance.
/// </summary>
public static class ProviderModelBroadcast
{
    /// <summary>
    /// Forces all entries in <paramref name="models"/> that share a
    /// <see cref="ProviderModel.ProviderType"/> to also share the broadcast fields.
    /// The first entry per ProviderType wins. Per-model fields are not touched.
    /// Idempotent — invoking it twice produces no further change.
    /// </summary>
    public static void Apply(IList<ProviderModel> models)
    {
        var seen = new Dictionary<string, ProviderModel>(System.StringComparer.Ordinal);
        foreach (var m in models)
        {
            if (!seen.TryGetValue(m.ProviderType, out var first))
            {
                seen[m.ProviderType] = m;
                continue;
            }
            m.MaxTokens = first.MaxTokens;
            m.Temperature = first.Temperature;
            m.StreamingEnabled = first.StreamingEnabled;
            m.PdfCapability = first.PdfCapability;
            m.ThinkingEnabled = first.ThinkingEnabled;
            m.ThinkingOverride = first.ThinkingOverride;
            m.ThinkingBudget = first.ThinkingBudget;
            // BaseUrl is broadcast — it's a Provider-level endpoint, not per-model.
            m.BaseUrl = first.BaseUrl;
        }
    }
}
