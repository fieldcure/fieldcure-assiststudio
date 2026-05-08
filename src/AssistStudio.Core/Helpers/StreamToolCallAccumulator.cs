using FieldCure.Ai.Providers.Models;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FieldCure.AssistStudio.Core.Helpers;

/// <summary>
/// Accumulates <see cref="StreamEvent.ToolCallStart"/> and <see cref="StreamEvent.ToolCallDelta"/>
/// events into complete <see cref="ToolCall"/> objects.
/// </summary>
public sealed class StreamToolCallAccumulator
{
    private readonly Dictionary<string, (string FunctionName, StringBuilder Arguments, string? ProviderSignature)> _pending = new();

    /// <summary>Registers a new tool call from a <see cref="StreamEvent.ToolCallStart"/> event.</summary>
    public void HandleStart(StreamEvent.ToolCallStart start)
        => _pending[start.Id] = (start.FunctionName, new StringBuilder(), start.ProviderSignature);

    /// <summary>Appends an argument chunk from a <see cref="StreamEvent.ToolCallDelta"/> event.</summary>
    public void HandleDelta(StreamEvent.ToolCallDelta delta)
    {
        if (_pending.TryGetValue(delta.Id, out var entry))
            entry.Arguments.Append(delta.ArgumentsChunk);
    }

    /// <summary>Whether any tool calls have been accumulated.</summary>
    public bool HasToolCalls => _pending.Count > 0;

    /// <summary>
    /// Returns the accumulated tool calls and resets internal state.
    /// </summary>
    /// <remarks>
    /// Entries whose accumulated argument JSON is unparseable are dropped silently.
    /// This happens when the stream is interrupted mid <c>input_json_delta</c>
    /// (e.g., user STOP) — the partial JSON cannot be sent to the provider on the
    /// next turn (Anthropic rejects, Gemini/OpenAI build-side <c>JsonNode.Parse</c>
    /// throws), and there is no coherent input from which a synthesized cancel
    /// <c>tool_result</c> could be derived.
    /// Empty/whitespace arguments are treated as <c>{}</c> and kept (matches each
    /// provider's send-build normalization).
    /// </remarks>
    public List<ToolCall> Drain()
    {
        var result = new List<ToolCall>(_pending.Count);
        foreach (var (id, (funcName, args, signature)) in _pending)
        {
            var argsStr = args.ToString();
            var normalized = string.IsNullOrWhiteSpace(argsStr) ? "{}" : argsStr;
            try { JsonNode.Parse(normalized); }
            catch (JsonException) { continue; }

            result.Add(new ToolCall
            {
                Id = id,
                FunctionName = funcName,
                Arguments = argsStr,
                ProviderSignature = signature,
            });
        }
        _pending.Clear();
        return result;
    }
}
