using System.Text.Json;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution;

/// <summary>
/// Default agent loop implementation using non-streaming CompleteAsync.
/// Extracted from Runner's TaskExecutor for shared use by Runner and SubAgentExecutor.
/// </summary>
public sealed class AgentLoop : IAgentLoop
{
    /// <summary>
    /// Optional log callback. Host applications can wire this to their
    /// logging infrastructure (e.g., <c>DiagnosticLogger.LogInfo</c>).
    /// When <c>null</c>, log messages are silently discarded.
    /// </summary>
    public Action<string>? LogCallback { get; set; }

    private void Log(string message) => LogCallback?.Invoke(message);

    /// <inheritdoc/>
    public async Task<AgentLoopResult> RunAsync(
        AgentLoopContext context,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, context.UserPrompt)
        };

        var toolList = context.Tools is { Count: > 0 }
            ? context.Tools.ToList<IAssistTool>()
            : null;

        var totalToolCalls = 0;
        string? lastSummary = null;
        AgentLoopStatus status = AgentLoopStatus.Failed;
        string? errorMessage = null;
        var roundsExecuted = 0;

        var forceFinish = false;

        try
        {
            Log(
                $"[AgentLoop] Starting: tools={toolList?.Count ?? 0}, maxRounds={context.MaxRounds}, "
                + $"systemPrompt={context.SystemPrompt?.Length ?? 0} chars, userPrompt={context.UserPrompt?.Length ?? 0} chars, "
                + $"provider={context.Provider.GetType().Name}");

            for (var round = 1; round <= context.MaxRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Level B context guard: check accumulated content size before LLM call.
                // Measured AFTER tool results are added (previous round), BEFORE next LLM call.
                var systemPromptForRound = context.SystemPrompt;
                var toolsForRound = toolList;

                if (context.MaxContextChars > 0)
                {
                    var totalChars = (context.SystemPrompt?.Length ?? 0)
                        + messages.Sum(m => m.Content?.Length ?? 0);

                    if (totalChars > context.MaxContextChars * 0.8)
                    {
                        Log(
                            $"[AgentLoop] Context limit reached: {totalChars:N0} chars "
                            + $"(threshold: {context.MaxContextChars * 0.8:N0}). Forcing summary.");

                        forceFinish = true;
                        // Inject summarization instruction (ephemeral — not stored in messages)
                        systemPromptForRound = context.SystemPrompt
                            + "\n\n[URGENT] Context limit approaching. "
                            + "Summarize your findings now and write your final report. "
                            + "Do not make any more tool calls.";
                        // Remove tools so LLM can only produce text
                        toolsForRound = null;
                    }
                }

                var request = new AiRequest
                {
                    Messages = messages,
                    SystemPrompt = systemPromptForRound,
                    Temperature = context.Temperature ?? 0.7,
                    MaxTokens = context.MaxTokens ?? 4096,
                    Tools = toolsForRound,
                };

                Log(
                    $"[AgentLoop] Round {round}: messages={messages.Count}, "
                    + $"totalContentChars={messages.Sum(m => m.Content?.Length ?? 0)}, "
                    + $"tools={toolsForRound?.Count ?? 0}{(forceFinish ? " (FORCE FINISH)" : "")}");

                var response = await context.Provider.CompleteAsync(request, cancellationToken);

                // No tool calls or forced finish → task complete.
                // BUT: if the provider cut the response off at max_tokens, the summary
                // is partial — never mistake that for graceful completion. Callers that
                // forward this to a parent conversation must be told so they don't
                // present a mid-markdown cutoff as a finished report.
                if (!response.HasToolCalls || forceFinish)
                {
                    lastSummary = response.Content?.Trim();
                    status = response.IsTruncated && !forceFinish
                        ? AgentLoopStatus.Truncated
                        : AgentLoopStatus.Completed;
                    roundsExecuted = round;
                    if (status == AgentLoopStatus.Truncated)
                    {
                        Log(
                            $"[AgentLoop] Response truncated at max_tokens in round {round} "
                            + $"(content={lastSummary?.Length ?? 0} chars). Reporting partial summary.");
                    }
                    break;
                }

                // Add assistant message with tool calls
                var assistantMsg = new ChatMessage(ChatRole.Assistant, response.Content ?? "")
                {
                    ToolCalls = response.ToolCalls.ToList(),
                };
                messages.Add(assistantMsg);

                // Execute tool calls
                foreach (var toolCall in response.ToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var toolResult = await ExecuteToolCallAsync(toolCall, toolList, cancellationToken);
                    totalToolCalls++;

                    // Guard against oversized tool results that would overflow the context window
                    if (context.MaxToolResultChars > 0 && toolResult.Length > context.MaxToolResultChars)
                    {
                        var originalLength = toolResult.Length;
                        toolResult = toolResult[..context.MaxToolResultChars]
                            + $"\n\n[Truncated: {originalLength:N0} → {context.MaxToolResultChars:N0} chars]";
                        Log(
                            $"[AgentLoop] Tool result truncated: {toolCall.FunctionName}, {originalLength:N0} → {context.MaxToolResultChars:N0} chars");
                    }

                    messages.Add(new ChatMessage(ChatRole.Tool, toolResult)
                    {
                        ToolCallId = toolCall.Id,
                    });
                }

                roundsExecuted = round;

                // Check if max rounds reached
                if (round == context.MaxRounds)
                {
                    status = AgentLoopStatus.MaxRoundsReached;
                    errorMessage = $"Maximum rounds ({context.MaxRounds}) reached without completion.";
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw; // Let caller handle timeout vs. user cancellation
        }
        catch (Exception ex)
        {
            status = AgentLoopStatus.Failed;
            errorMessage = ex.Message;
        }

        return new AgentLoopResult
        {
            Status = status,
            RoundsExecuted = roundsExecuted,
            Summary = lastSummary,
            ErrorMessage = errorMessage,
            ToolCallCount = totalToolCalls,
            Messages = messages,
        };
    }

    static async Task<string> ExecuteToolCallAsync(
        ToolCall toolCall,
        IReadOnlyList<IAssistTool>? tools,
        CancellationToken cancellationToken)
    {
        var tool = tools?.FirstOrDefault(t => t.Name == toolCall.FunctionName);
        if (tool is null)
            return $"Error: Tool '{toolCall.FunctionName}' not found.";

        try
        {
            var args = JsonDocument.Parse(toolCall.Arguments).RootElement;
            return await tool.ExecuteAsync(args, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Error executing tool '{toolCall.FunctionName}': {ex.Message}";
        }
    }
}
