using System.Text.Json;

namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Extends <see cref="IAssistTool"/> to support tool results containing
/// multimedia content (e.g. images) in addition to text.
/// </summary>
/// <remarks>
/// MCP tools may return <c>ImageContentBlock</c> alongside <c>TextContentBlock</c>.
/// Implementing this interface allows the tool execution pipeline to propagate
/// image data without mutable state on the tool adapter.
/// </remarks>
public interface IMultiContentTool : IAssistTool
{
    /// <summary>
    /// Executes the tool and returns a <see cref="ToolExecutionResult"/> containing
    /// both the text result and any multimedia content.
    /// </summary>
    Task<ToolExecutionResult> ExecuteWithContentAsync(JsonElement parameters, CancellationToken ct = default);
}
