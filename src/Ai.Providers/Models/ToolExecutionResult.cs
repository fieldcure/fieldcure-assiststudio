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
public record ToolExecutionResult(string Text, IReadOnlyList<MediaContent>? MediaContents = null);
