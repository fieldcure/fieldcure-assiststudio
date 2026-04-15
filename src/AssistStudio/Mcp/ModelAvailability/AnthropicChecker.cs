using AssistStudio.Helpers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Reports whether an Anthropic (Claude) model is usable based on
/// credential vault presence. Same approach as <see cref="OpenAiChecker"/>:
/// local key lookup only, no <c>/v1/models</c> probe. Credential vault
/// preset name is the literal "Claude".
/// </summary>
public sealed class AnthropicChecker : IModelAvailabilityChecker
{
    private readonly ResourceLoader _loader = new();
    private bool? _hasKey;

    public string ProviderName => "anthropic";

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
        try { return PasswordVaultHelper.HasApiKey("Claude"); }
        catch { return false; }
    }
}
