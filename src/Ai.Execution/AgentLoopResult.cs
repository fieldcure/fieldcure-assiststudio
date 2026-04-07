using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution;

/// <summary>
/// Result of an agent loop execution.
/// </summary>
public sealed class AgentLoopResult
{
    /// <summary>
    /// How the loop terminated.
    /// </summary>
    public required AgentLoopStatus Status { get; init; }

    /// <summary>
    /// Number of LLM call rounds executed.
    /// </summary>
    public int RoundsExecuted { get; init; }

    /// <summary>
    /// Last assistant message content (trimmed).
    /// For sub-agents, this is the report text.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Error description when <see cref="Status"/> is <see cref="AgentLoopStatus.Failed"/>.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total number of tool calls executed across all rounds.
    /// </summary>
    public int ToolCallCount { get; init; }

    /// <summary>
    /// Full conversation messages accumulated during the loop.
    /// Includes user, assistant (with tool calls), and tool result messages.
    /// Available for detailed logging and audit trails.
    /// </summary>
    public IReadOnlyList<ChatMessage>? Messages { get; init; }
}

/// <summary>
/// How the agent loop terminated.
/// Timeout is not included — the caller handles timeout via <see cref="CancellationToken"/>
/// and maps <see cref="OperationCanceledException"/> to the appropriate status.
/// </summary>
public enum AgentLoopStatus
{
    /// <summary>LLM responded with no tool calls — task complete.</summary>
    Completed,

    /// <summary>Maximum rounds reached without LLM completing naturally.</summary>
    MaxRoundsReached,

    /// <summary>An unrecoverable error occurred during execution.</summary>
    Failed
}
