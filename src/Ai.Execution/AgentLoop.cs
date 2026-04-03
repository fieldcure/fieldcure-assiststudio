using System.Text.Json;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution;

/// <summary>
/// Default agent loop implementation using non-streaming CompleteAsync.
/// Extracted from Runner's TaskExecutor for shared use by Runner and SubAgentExecutor.
/// </summary>
public sealed class AgentLoop : IAgentLoop
{
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

        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[AgentLoop] Starting: tools={toolList?.Count ?? 0}, maxRounds={context.MaxRounds}, "
                + $"systemPrompt={context.SystemPrompt?.Length ?? 0} chars, userPrompt={context.UserPrompt?.Length ?? 0} chars, "
                + $"provider={context.Provider.GetType().Name}");

            for (var round = 1; round <= context.MaxRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var request = new AiRequest
                {
                    Messages = messages,
                    SystemPrompt = context.SystemPrompt,
                    Temperature = context.Temperature ?? 0.7,
                    MaxTokens = context.MaxTokens ?? 4096,
                    Tools = toolList,
                };

                System.Diagnostics.Debug.WriteLine(
                    $"[AgentLoop] Round {round}: messages={messages.Count}, "
                    + $"totalContentChars={messages.Sum(m => m.Content?.Length ?? 0)}, "
                    + $"tools={toolList?.Count ?? 0}");

                var response = await context.Provider.CompleteAsync(request, cancellationToken);

                // No tool calls → task complete
                if (!response.HasToolCalls)
                {
                    lastSummary = response.Content?.Trim();
                    status = AgentLoopStatus.Completed;
                    roundsExecuted = round;
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
                        System.Diagnostics.Debug.WriteLine(
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
