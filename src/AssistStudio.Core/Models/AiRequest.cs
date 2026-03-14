namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Represents a request to an AI provider, including messages and generation parameters.
/// </summary>
public partial class AiRequest
{
    /// <summary>The conversation messages to send to the AI provider.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>An optional system prompt that sets the behavior of the assistant.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Sampling temperature controlling randomness (0.0 = deterministic, 2.0 = most random). Default is 0.7.</summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>Maximum number of tokens the model can generate in the response. Default is 4096.</summary>
    public int MaxTokens { get; init; } = 4096;
}
