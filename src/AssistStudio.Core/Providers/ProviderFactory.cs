using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// Factory for creating <see cref="IAiProvider"/> instances from a <see cref="ProviderPreset"/> configuration.
/// </summary>
public static class ProviderFactory
{
    #region Public Methods

    /// <summary>
    /// Creates an <see cref="IAiProvider"/> instance based on the specified preset configuration.
    /// </summary>
    public static IAiProvider Create(ProviderPreset preset)
    {
        var apiKey = preset.ApiKey;
        var model = preset.ModelId;

        return preset.ProviderType switch
        {
            "Claude" => string.IsNullOrEmpty(model)
                ? new ClaudeProvider(apiKey)
                : new ClaudeProvider(apiKey, model),
            "OpenAI" => string.IsNullOrEmpty(model)
                ? new OpenAiProvider(apiKey)
                : new OpenAiProvider(apiKey, model),
            "Ollama" => string.IsNullOrEmpty(model)
                ? new OllamaProvider()
                : new OllamaProvider(model),
            "Gemini" => string.IsNullOrEmpty(model)
                ? new GeminiProvider(apiKey)
                : new GeminiProvider(apiKey, model),
            "Groq" => new OpenAiProvider(
                apiKey,
                string.IsNullOrEmpty(model) ? "llama-3.3-70b-versatile" : model,
                "https://api.groq.com/openai/v1",
                "Groq",
                PdfCapability.TextExtraction),
            _ => new MockProvider()
        };
    }

    #endregion

    #region Constants

    /// <summary>The list of all supported provider type keys.</summary>
    public static readonly string[] ProviderTypes =
        ["Mock", "Claude", "OpenAI", "Ollama", "Gemini", "Groq"];

    #endregion
}
