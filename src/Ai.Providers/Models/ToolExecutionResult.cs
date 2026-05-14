using System.Text.Json;

namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Represents the result of a tool execution, containing the text result
/// and optional multimedia content (images, audio, video, downloadable files).
/// </summary>
/// <param name="Text">The text result string for the AI model.</param>
/// <param name="MediaContents">
/// Optional list of <see cref="MediaContent"/> items extracted from MCP content blocks
/// (e.g. <c>ImageContentBlock</c>, <c>AudioContentBlock</c>, <c>EmbeddedResourceBlock</c>).
/// </param>
/// <param name="StructuredContent">
/// Optional <c>structuredContent</c> payload from an MCP tool result (MCP spec
/// <c>CallToolResult.structuredContent</c>). Unlike <paramref name="Text"/>, this
/// is not fed to the model — it is a host-side rendering channel. The chat panel
/// inspects it for a <c>chart</c> object (e.g. a Plotly spec shipped by
/// <c>ls_get_chart</c>) and renders it inline without any token cost.
/// </param>
public record ToolExecutionResult(
    string Text,
    IReadOnlyList<MediaContent>? MediaContents = null,
    JsonElement? StructuredContent = null);
