namespace FieldCure.Ai.Execution;

/// <summary>
/// Core LLM execution loop: prompt → model call → tool execution → repeat.
/// The loop continues until the LLM responds without tool calls, max rounds
/// is reached, or cancellation is requested.
/// </summary>
public interface IAgentLoop
{
    /// <summary>
    /// Runs the agent loop until completion, max rounds, or cancellation.
    /// </summary>
    /// <param name="context">Loop configuration including provider, prompt, tools, and guards.</param>
    /// <param name="cancellationToken">
    /// Cancellation token. Callers should use this for timeout enforcement
    /// (e.g., <c>CancellationTokenSource.CancelAfter</c>).
    /// </param>
    /// <returns>Result describing how the loop terminated and its output.</returns>
    Task<AgentLoopResult> RunAsync(
        AgentLoopContext context,
        CancellationToken cancellationToken = default);
}
