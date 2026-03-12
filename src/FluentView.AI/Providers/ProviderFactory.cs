using FluentView.AI.Models;

namespace FluentView.AI.Providers;

public static class ProviderFactory
{
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
                "Groq"),
            _ => new MockProvider()
        };
    }

    public static readonly string[] ProviderTypes =
        ["Mock", "Claude", "OpenAI", "Ollama", "Gemini", "Groq"];
}
