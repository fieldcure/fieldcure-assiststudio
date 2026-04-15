using FieldCure.Ai.Providers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Queries the local Ollama daemon's <c>/api/tags</c> endpoint once per
/// checker instance and answers <see cref="CheckModelAsync"/> against the
/// returned installed-model set.
/// </summary>
/// <remarks>
/// Caches the tag fetch in a shared <see cref="Task"/> so concurrent
/// callers (the KB list refresh checks multiple KBs in parallel) do not
/// each issue their own HTTP request. If the Ollama daemon is not running
/// we return <c>null</c> from every <see cref="CheckModelAsync"/> call —
/// the dialog should not flag every Ollama model as missing just because
/// the service is temporarily down.
/// </remarks>
public sealed class OllamaChecker : IModelAvailabilityChecker
{
    private readonly string _baseUrl;
    private readonly ResourceLoader _loader = new();
    private Task<IReadOnlySet<string>?>? _installedTask;
    private readonly object _gate = new();

    public OllamaChecker(string baseUrl = "http://localhost:11434")
    {
        _baseUrl = baseUrl;
    }

    public string ProviderName => "ollama";

    public async Task<string?> CheckModelAsync(string modelId, CancellationToken ct = default)
    {
        var installed = await GetInstalledAsync(ct);
        if (installed is null)
            return null; // probe failed → permissive

        if (installed.Contains(modelId))
            return null;

        // Some Ollama tags carry a quant suffix like "qwen3-embedding:8b-q4_K_M"
        // while the catalog id is just "qwen3-embedding:8b". Accept either
        // direction so the selector's hardcoded ids still match the daemon.
        var colon = modelId.IndexOf(':');
        var bare = colon >= 0 ? modelId[..colon] : modelId;
        if (installed.Contains(bare))
            return null;

        return _loader.GetString("KB_ModelNotInstalled") ?? "(not installed)";
    }

    private Task<IReadOnlySet<string>?> GetInstalledAsync(CancellationToken ct)
    {
        // Shared-task pattern: first caller starts the probe, subsequent
        // callers await the same task. Null result means "probe failed,
        // treat everything as available".
        lock (_gate)
        {
            _installedTask ??= FetchAsync(ct);
        }
        return _installedTask;
    }

    private async Task<IReadOnlySet<string>?> FetchAsync(CancellationToken ct)
    {
        try
        {
            using var manager = new OllamaModelManager(_baseUrl);
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
}
