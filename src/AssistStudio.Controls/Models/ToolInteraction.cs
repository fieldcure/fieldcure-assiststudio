using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// One model-issued <see cref="ToolCall"/> paired with the JSON result returned by the
/// consumer's <see cref="IAssistTool.ExecuteAsync"/> invocation. Supplied to
/// <see cref="ChatPanel.AppendToolRoundAsync"/> so the chat surface can render an inline
/// tool block under the active assistant turn.
/// </summary>
/// <param name="Call">The tool call emitted by the model.</param>
/// <param name="Result">The tool's JSON output (or an error envelope on failure).</param>
/// <param name="IsError">Whether the result represents a tool execution failure.</param>
public sealed record ToolInteraction(ToolCall Call, string Result, bool IsError = false);
