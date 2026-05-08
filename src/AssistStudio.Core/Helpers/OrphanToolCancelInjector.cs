using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Core.Helpers;

/// <summary>
/// Reorders <see cref="ChatRole.Tool"/> messages following an assistant turn so they
/// match the assistant's <see cref="ChatMessage.ToolCalls"/> declaration order, and
/// synthesizes a <c>"[Stopped by user]"</c> tool result for any orphan tool call that
/// has no matching follower (e.g., a tool call whose execution was cancelled before
/// it produced a result).
/// </summary>
/// <remarks>
/// Order preservation is mandatory for providers that match tool results
/// positionally (Ollama native <c>/api/chat</c>) or by function name with index
/// suffix stripping (Gemini). OpenAI's strict <c>tool_call_id</c> matching is
/// order-insensitive but unaffected by reordering.
/// </remarks>
public static class OrphanToolCancelInjector
{
    /// <summary>The synthetic tool-result body inserted for orphan tool calls.</summary>
    public const string CanceledToolResultContent = "[Stopped by user]";

    /// <summary>
    /// Returns a new message list with orphan tool calls filled in by synthesized
    /// cancel tool results. The input list is not mutated; existing messages are
    /// reused by reference.
    /// </summary>
    public static IReadOnlyList<ChatMessage> Inject(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<ChatMessage>(messages.Count);
        var synthesizedCount = 0;
        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            result.Add(msg);

            if (msg.Role != ChatRole.Assistant || msg.ToolCalls is not { Count: > 0 })
                continue;

            // Collect contiguous trailing Tool messages, index by ToolCallId.
            // Followers whose ToolCallId is not present in msg.ToolCalls are dropped.
            // In well-formed conversations this never occurs; future message-editing
            // features should validate orphan tool_results separately.
            var followers = new Dictionary<string, ChatMessage>(StringComparer.Ordinal);
            var j = i + 1;
            while (j < messages.Count && messages[j].Role == ChatRole.Tool)
            {
                if (messages[j].ToolCallId is { Length: > 0 } id && !followers.ContainsKey(id))
                    followers[id] = messages[j];
                j++;
            }

            foreach (var tc in msg.ToolCalls)
            {
                if (followers.TryGetValue(tc.Id, out var matched))
                    result.Add(matched);
                else
                {
                    result.Add(new ChatMessage(ChatRole.Tool, CanceledToolResultContent)
                    {
                        ToolCallId = tc.Id
                    });
                    synthesizedCount++;
                }
            }

            i = j - 1; // skip the original follower span — already emitted in corrected order
        }

        if (synthesizedCount > 0)
            DiagnosticLogger.LogInfo($"[Cancel] Synthesized {synthesizedCount} orphan tool_result(s) for outgoing request.");

        return result;
    }
}
