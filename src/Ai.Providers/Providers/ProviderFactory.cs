using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers;

/// <summary>
/// Factory for creating <see cref="IAiProvider"/> instances from a <see cref="ProviderPreset"/> configuration.
/// </summary>
public static class ProviderFactory
{
    #region Fields

    /// <summary>Registry of custom provider configurations keyed by "Custom_{id}".</summary>
    private static readonly Dictionary<string, CustomProviderConfig> _customConfigs = new();

    #endregion

    #region Custom Provider Registration

    /// <summary>Registers a custom provider configuration for use in <see cref="Create"/>.</summary>
    public static void RegisterCustomProvider(CustomProviderConfig config)
        => _customConfigs[$"Custom_{config.Id}"] = config;

    /// <summary>Removes a custom provider configuration.</summary>
    public static void UnregisterCustomProvider(string id)
        => _customConfigs.Remove($"Custom_{id}");

    /// <summary>Removes all custom provider configurations.</summary>
    public static void ClearCustomProviders()
        => _customConfigs.Clear();

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates an <see cref="IAiProvider"/> instance based on the specified preset configuration.
    /// </summary>
    public static IAiProvider Create(ProviderPreset preset)
    {
        var apiKey = preset.ApiKey;
        var model = preset.ModelId;
        var pdfCap = ResolvePdfCapability(preset);

        // Custom providers: always OpenAI-compatible
        if (preset.ProviderType.StartsWith("Custom_")
            && _customConfigs.TryGetValue(preset.ProviderType, out var config))
        {
            if (string.IsNullOrEmpty(model))
                throw new InvalidOperationException(
                    $"No model configured for custom provider '{config.DisplayName}'. Please set a model ID in Settings → Models.");

            return new OpenAiProvider(
                apiKey,
                model,
                config.BaseUrl,
                config.DisplayName,
                pdfCap);
        }

        return preset.ProviderType switch
        {
            "Claude" => string.IsNullOrEmpty(model)
                ? new ClaudeProvider(apiKey)
                : new ClaudeProvider(apiKey, model),
            "OpenAI" => string.IsNullOrEmpty(model)
                ? new OpenAiProvider(apiKey, pdfCapability: pdfCap)
                : new OpenAiProvider(apiKey, model, pdfCapability: pdfCap),
            "Ollama" => string.IsNullOrEmpty(model)
                ? new OllamaProvider(baseUrl: preset.BaseUrl ?? "http://localhost:11434", pdfCapability: pdfCap)
                : new OllamaProvider(model, baseUrl: preset.BaseUrl ?? "http://localhost:11434", pdfCapability: pdfCap),
            "Gemini" => string.IsNullOrEmpty(model)
                ? new GeminiProvider(apiKey)
                : new GeminiProvider(apiKey, model),
            "Groq" => new OpenAiProvider(
                apiKey,
                string.IsNullOrEmpty(model) ? "llama-3.3-70b-versatile" : model,
                "https://api.groq.com/openai/v1",
                "Groq",
                pdfCap),
            _ => new MockProvider()
        };
    }

    /// <summary>
    /// Resolves the effective <see cref="PdfCapability"/> for a preset,
    /// applying provider-type defaults when set to <see cref="PdfCapability.Auto"/>.
    /// </summary>
    private static PdfCapability ResolvePdfCapability(ProviderPreset preset)
    {
        if (preset.PdfCapability != PdfCapability.Auto)
            return preset.PdfCapability;

        if (preset.ProviderType.StartsWith("Custom_"))
            return PdfCapability.TextExtraction;

        return preset.ProviderType switch
        {
            "Claude" or "OpenAI" or "Gemini" => PdfCapability.NativePdf,
            _ => PdfCapability.TextExtraction
        };
    }

    #endregion

    #region Constants

    /// <summary>The list of all supported provider type keys.</summary>
    public static readonly string[] ProviderTypes =
        ["Mock", "Claude", "OpenAI", "Ollama", "Gemini", "Groq"];

    #endregion
}
