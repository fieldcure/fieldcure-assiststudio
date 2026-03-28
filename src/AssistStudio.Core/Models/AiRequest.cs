namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Represents a request to an AI provider, including messages and generation parameters.
/// </summary>
public partial class AiRequest
{
    #region Properties

    /// <summary>The conversation messages to send to the AI provider.</summary>
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    /// <summary>An optional system prompt that sets the behavior of the assistant.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Sampling temperature controlling randomness (0.0 = deterministic, 2.0 = most random). Default is 0.7.</summary>
    public double Temperature { get; init; } = 0.7;

    /// <summary>Maximum number of tokens the model can generate in the response. Default is 4096.</summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>Optional list of tools available for the AI model to invoke.</summary>
    public IReadOnlyList<IAssistTool>? Tools { get; init; }

    /// <summary>Retrieved context chunks for RAG injection into the system prompt.</summary>
    public IReadOnlyList<ContextChunk>? ContextChunks { get; init; }

    /// <summary>Current workspace state text from <see cref="IWorkspaceContext"/>.</summary>
    public string? WorkspaceText { get; init; }

    /// <summary>Persistent memory text to inject into the system prompt.</summary>
    public string? MemoryText { get; init; }

    /// <summary>Whether extended thinking/reasoning is enabled.</summary>
    public bool ThinkingEnabled { get; init; }

    /// <summary>Thinking budget in tokens. Null uses provider default.</summary>
    public int? ThinkingBudget { get; init; }

    #endregion
}
