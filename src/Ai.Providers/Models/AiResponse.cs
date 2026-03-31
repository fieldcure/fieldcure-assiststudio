namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Represents a complete response from an AI provider, which may contain text content, tool calls, or both.
/// </summary>
public class AiResponse
{
    #region Properties

    /// <summary>The text content of the response, or <see langword="null"/> if the response only contains tool calls.</summary>
    public string? Content { get; init; }

    /// <summary>Tool calls requested by the AI model. Empty if the response is text-only.</summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = [];

    /// <summary>Token usage for this response, or <see langword="null"/> if unavailable.</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>Whether the response was truncated due to max token limits.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>Thinking/reasoning content from the AI model, or <see langword="null"/> if not available.</summary>
    public string? ThinkingContent { get; init; }

    /// <summary>Whether this response contains one or more tool calls.</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;

    #endregion
}
