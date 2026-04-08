namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Configuration for a user-registered OpenAI-compatible custom provider.
/// </summary>
public class CustomProviderConfig
{
    /// <summary>Unique identifier (GUID string).</summary>
    public string Id { get; set; } = "";

    /// <summary>User-facing display name (e.g., "Together AI").</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// Base URL for the OpenAI-compatible API endpoint.
    /// Must not include trailing slash or path segments like /chat/completions.
    /// Example: "https://api.together.xyz/v1"
    /// </summary>
    public string BaseUrl { get; set; } = "";
}
