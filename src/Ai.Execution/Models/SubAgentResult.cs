namespace FieldCure.Ai.Execution.Models;

/// <summary>
/// Result returned from a sub-agent execution.
/// </summary>
public sealed class SubAgentResult
{
    /// <summary>
    /// The sub-agent's final report text.
    /// Sourced from the last assistant message in the agent loop.
    /// </summary>
    public required string Report { get; init; }

    /// <summary>
    /// Execution status.
    /// </summary>
    public SubAgentStatus Status { get; init; }

    /// <summary>
    /// Total number of tool calls made during execution.
    /// </summary>
    public int ToolCallCount { get; init; }

    /// <summary>
    /// Total execution duration.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Number of LLM call rounds executed.
    /// </summary>
    public int RoundsExecuted { get; init; }

    /// <summary>
    /// Provider preset that was actually used.
    /// </summary>
    public required string? UsedPreset { get; init; }
}

/// <summary>
/// Sub-agent execution status.
/// </summary>
public enum SubAgentStatus
{
    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Execution exceeded the configured timeout.</summary>
    TimedOut,

    /// <summary>Maximum rounds reached without natural completion.</summary>
    MaxRoundsReached,

    /// <summary>An error occurred during execution.</summary>
    Failed
}
