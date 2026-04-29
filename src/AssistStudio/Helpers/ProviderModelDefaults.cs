namespace AssistStudio.Helpers;

/// <summary>
/// Hardcoded fallback ModelIds used to seed <see cref="FieldCure.Ai.Providers.Models.ProviderModel"/>
/// instances during first-launch / first-key-add when no cached upstream model list exists.
///
/// <para>
/// These values reflect the latest known-good model on each provider as of writing.
/// Users override them via the Models page checklist after first launch — these
/// constants only matter when there is literally no signal to choose from.
/// </para>
/// </summary>
internal static class ProviderModelDefaults
{
    /// <summary>Returns the default <c>ModelId</c> for the given provider, or <c>null</c> for unknown providers.</summary>
    public static string? ForProvider(string providerType) => providerType switch
    {
        "Claude" => "claude-sonnet-4-6",
        "OpenAI" => "gpt-5",
        "Gemini" => "gemini-2.5-pro",
        "Groq" => "llama-3.3-70b-versatile",
        "Ollama" => "llama3.3",
        "Mock" => AppSettings.MockDefaultModelId,
        _ => null,
    };
}
