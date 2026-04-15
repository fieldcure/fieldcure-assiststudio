using AssistStudio.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Reports whether an OpenAI-routed model is usable based on credential
/// vault presence. Does not probe <c>/v1/models</c> — a stale or revoked
/// key would only surface as a 401 at exec time, and that path is already
/// handled by v1.4.2's <c>ProviderHealth.EmbeddingUnavailable</c> flag.
/// The cheap local check (is there a key at all?) is what this class
/// provides.
/// </summary>
/// <remarks>
/// PasswordVault access is not free (WinRT COM interop) so the result is
/// cached per checker instance. The credential vault preset name used
/// throughout the app for OpenAI is the literal string "OpenAI".
/// </remarks>
public sealed class OpenAiChecker : IModelAvailabilityChecker
{
    private readonly ResourceLoader _loader = new();
    private bool? _hasKey;

    public string ProviderName => "openai";

    public Task<string?> CheckModelAsync(string modelId, CancellationToken ct = default)
    {
        _hasKey ??= ProbeKey();
        if (_hasKey == true)
            return Task.FromResult<string?>(null);

        var reason = _loader.GetString("KB_ModelMissingApiKey") ?? "(API key missing)";
        return Task.FromResult<string?>(reason);
    }

    private static bool ProbeKey()
    {
        try { return PasswordVaultHelper.HasApiKey("OpenAI"); }
        catch { return false; }
    }
}
