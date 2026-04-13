using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Aggregated result returned after consuming a streaming response.
/// </summary>
/// <param name="Usage">Token usage information, if reported by the stream.</param>
/// <param name="IsTruncated">Whether the response was truncated due to max token limits.</param>
/// <param name="ToolCalls">Tool calls requested by the assistant, if any.</param>
public sealed record StreamResult(TokenUsage? Usage, bool IsTruncated, IReadOnlyList<ToolCall>? ToolCalls = null)
{
    /// <summary>Gets whether the stream produced any tool calls.</summary>
    public bool HasToolCalls => ToolCalls is { Count: > 0 };
}
