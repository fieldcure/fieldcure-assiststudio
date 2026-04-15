namespace AssistStudio.Mcp.ModelAvailability;

/// <summary>
/// Per-provider check that answers "is this specific model usable right now"
/// without claiming to know the catalog of every model the provider supports.
/// An implementation is expected to cache its underlying probe
/// (<c>/api/tags</c> for Ollama, credential lookup for cloud providers) so
/// repeat calls on the same instance do not multiply network round-trips.
/// </summary>
/// <remarks>
/// The return value is a nullable reason string: <c>null</c> means the
/// model is available, any other value is a short, user-visible explanation
/// of why it is not (e.g. "(설치 안 됨)", "(API 키 없음)"). Implementations
/// that cannot verify availability due to an infrastructure failure (Ollama
/// daemon down, network error) must return <c>null</c> rather than a false
/// positive — we would rather under-warn than block a dialog on a transient
/// probe error.
/// </remarks>
public interface IModelAvailabilityChecker
{
    /// <summary>
    /// Provider identifier this checker handles, matching the value stored
    /// in <c>KbProviderConfig.Provider</c> (e.g. "ollama", "openai",
    /// "anthropic"). Case-insensitive.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Returns <c>null</c> if <paramref name="modelId"/> is currently
    /// usable, or a short user-visible reason string if it is not.
    /// </summary>
    Task<string?> CheckModelAsync(string modelId, CancellationToken ct = default);
}
