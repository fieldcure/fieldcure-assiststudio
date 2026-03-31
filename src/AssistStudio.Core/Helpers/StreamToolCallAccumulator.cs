using System.Text;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Accumulates <see cref="StreamEvent.ToolCallStart"/> and <see cref="StreamEvent.ToolCallDelta"/>
/// events into complete <see cref="ToolCall"/> objects.
/// </summary>
public sealed class StreamToolCallAccumulator
{
    private readonly Dictionary<string, (string FunctionName, StringBuilder Arguments)> _pending = new();

    /// <summary>Registers a new tool call from a <see cref="StreamEvent.ToolCallStart"/> event.</summary>
    public void HandleStart(StreamEvent.ToolCallStart start)
        => _pending[start.Id] = (start.FunctionName, new StringBuilder());

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
    public List<ToolCall> Drain()
    {
        var result = new List<ToolCall>(_pending.Count);
        foreach (var (id, (funcName, args)) in _pending)
        {
            result.Add(new ToolCall
            {
                Id = id,
                FunctionName = funcName,
                Arguments = args.ToString()
            });
        }
        _pending.Clear();
        return result;
    }
}
