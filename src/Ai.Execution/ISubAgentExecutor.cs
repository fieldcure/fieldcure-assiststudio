using FieldCure.Ai.Execution.Models;

namespace FieldCure.Ai.Execution;

/// <summary>
/// Executes an isolated LLM session as a sub-agent and returns a compacted result.
/// The executor handles system prompt assembly (including <see cref="SubAgentRequest.ContextHints"/>),
/// timeout management, and agent loop orchestration.
/// </summary>
public interface ISubAgentExecutor
{
    /// <summary>
    /// Creates an isolated session, runs the agent loop, and returns the sub-agent's report.
    /// </summary>
    /// <param name="request">Sub-agent task definition.</param>
    /// <param name="cancellationToken">External cancellation (e.g., user cancel).</param>
    Task<SubAgentResult> ExecuteAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken = default);
}
