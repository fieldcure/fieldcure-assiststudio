using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers;

/// <summary>
/// Routes thinking support queries to the appropriate provider's static method.
/// Provides a unified entry point for UI and other consumers that don't have a provider instance.
/// </summary>
public static class ThinkingCapability
{
    /// <summary>
    /// Returns the thinking support level for the given model by delegating to the provider's static method.
    /// </summary>
    /// <param name="providerType">The provider type (e.g., "Claude", "OpenAI", "Gemini").</param>
    /// <param name="modelId">The model identifier.</param>
    /// <returns>The thinking support level for the model.</returns>
    public static ThinkingSupport GetSupport(string providerType, string? modelId) =>
        providerType switch
        {
            "Claude" => ClaudeProvider.GetThinkingSupportFor(modelId),
            "OpenAI" or "Groq" => OpenAiProvider.GetThinkingSupportFor(modelId),
            "Gemini" => GeminiProvider.GetThinkingSupportFor(modelId),
            "Ollama" => OllamaProvider.GetThinkingSupportFor(modelId),
            "Mock" => MockProvider.GetThinkingSupportFor(modelId),
            _ => ThinkingSupport.NotSupported
        };
}
