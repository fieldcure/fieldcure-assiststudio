using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution;

/// <summary>
/// Input context for a single agent loop execution.
/// Timeout is managed by the caller via <see cref="CancellationToken"/>.
/// </summary>
public sealed class AgentLoopContext
{
    /// <summary>
    /// AI provider to use for LLM calls.
    /// </summary>
    public required IAiProvider Provider { get; init; }

    /// <summary>
    /// Fully assembled system prompt. The loop uses this as-is.
    /// </summary>
    public required string SystemPrompt { get; init; }

    /// <summary>
    /// Initial user prompt that starts the conversation.
    /// </summary>
    public required string UserPrompt { get; init; }

    /// <summary>
    /// Tools available for the loop. Null or empty = no tool use.
    /// Callers are responsible for filtering (allowlist, MCP bootstrap, etc.).
    /// </summary>
    public IReadOnlyList<IAssistTool>? Tools { get; init; }

    /// <summary>
    /// Maximum rounds of LLM calls before forcing completion.
    /// </summary>
    public int MaxRounds { get; init; } = 10;

    /// <summary>
    /// Sampling temperature. Null = provider default.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// Maximum output tokens per LLM call. Null = provider default.
    /// </summary>
    public int? MaxTokens { get; init; }
}
