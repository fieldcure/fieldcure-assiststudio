using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// Defines the contract for an AI chat completion provider that supports both synchronous and streaming responses.
/// </summary>
public interface IAiProvider
{
    #region Properties

    /// <summary>The display name of this provider (e.g., "Claude", "OpenAI").</summary>
    string ProviderName { get; }

    /// <summary>The model identifier currently configured for this provider.</summary>
    string ModelId { get; }

    /// <summary>Token usage from the most recent request, or <see langword="null"/> if unavailable.</summary>
    TokenUsage? LastUsage { get; }

    /// <summary>Whether the most recent response was truncated due to max token limits.</summary>
    bool IsTruncated { get; }

    /// <summary>The raw JSON request body sent in the most recent API call, for debugging purposes.</summary>
    string? LastRequestBody { get; }

    /// <summary>The raw response body received from the most recent API call, for debugging purposes.</summary>
    string? LastRawResponse { get; }

    /// <summary>Declares how this provider handles PDF document attachments.</summary>
    PdfCapability PdfCapability { get; }

    #endregion

    #region Methods

    /// <summary>Sends a chat completion request and returns a structured response that may contain text, tool calls, or both.</summary>
    Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>Sends a chat completion request and streams response events as they arrive.</summary>
    IAsyncEnumerable<StreamEvent> StreamAsync(AiRequest request, CancellationToken ct = default);

    /// <summary>Lists all models available from this provider.</summary>
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);

    /// <summary>Validates the connection and credentials for this provider.</summary>
    Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default);

    #endregion
}
