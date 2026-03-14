using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// Defines the contract for an AI chat completion provider that supports both synchronous and streaming responses.
/// </summary>
public interface IAiProvider
{
    /// <summary>The display name of this provider (e.g., "Claude", "OpenAI").</summary>
    string ProviderName { get; }

    /// <summary>The model identifier currently configured for this provider.</summary>
    string ModelId { get; }

    /// <summary>Sends a chat completion request and returns the full response text.</summary>
    /// <param name="request">The AI request containing messages and parameters.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The complete response text from the model.</returns>
    Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>Sends a chat completion request and streams response tokens as they arrive.</summary>
    /// <param name="request">The AI request containing messages and parameters.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>An async enumerable of text chunks.</returns>
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>Token usage from the most recent request, or <see langword="null"/> if unavailable.</summary>
    TokenUsage? LastUsage { get; }

    /// <summary>Whether the most recent response was truncated due to max token limits.</summary>
    bool IsTruncated { get; }

    /// <summary>The raw JSON request body sent in the most recent API call, for debugging purposes.</summary>
    string? LastRequestBody { get; }

    /// <summary>The raw response body received from the most recent API call, for debugging purposes.</summary>
    string? LastRawResponse { get; }

    /// <summary>Lists all models available from this provider.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of available AI models.</returns>
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Validates the connection and credentials for this provider.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A <see cref="ConnectionInfo"/> indicating whether the connection is valid.</returns>
    Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default);
}
