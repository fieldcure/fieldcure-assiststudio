namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Represents the result of a tool execution, containing the text result
/// and optional multimedia content such as images.
/// </summary>
/// <param name="Text">The text result string for the AI model.</param>
/// <param name="ImageDataUris">
/// Optional list of image data URIs (e.g. <c>data:image/png;base64,...</c>)
/// extracted from MCP <c>ImageContentBlock</c> responses.
/// </param>
public record ToolExecutionResult(string Text, IReadOnlyList<string>? ImageDataUris = null);
