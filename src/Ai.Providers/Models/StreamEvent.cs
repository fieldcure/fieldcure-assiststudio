namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Base type for all events yielded during a streaming AI response.
/// Use C# pattern matching to handle specific event subtypes.
/// </summary>
/// <remarks>
/// This is a closed discriminated union — external subclassing is prevented by the private constructor.
/// Consumers should always include a default/discard arm in switch expressions to handle future subtypes gracefully.
/// </remarks>
public abstract record StreamEvent
{
    /// <summary>Prevents external subclassing so the union stays closed to the nested subtypes below.</summary>
    private StreamEvent() { }

    /// <summary>A chunk of text content from the assistant.</summary>
    /// <param name="Text">The text fragment.</param>
    public sealed record TextDelta(string Text) : StreamEvent;

    /// <summary>A chunk of thinking/reasoning content (e.g., Claude extended thinking, OpenAI o-series reasoning).</summary>
    /// <param name="Text">The thinking text fragment.</param>
    public sealed record ThinkingDelta(string Text) : StreamEvent;

    /// <summary>Signals the start of a streamed tool call.</summary>
    /// <param name="Id">The tool call identifier.</param>
    /// <param name="FunctionName">The function name being invoked.</param>
    /// <param name="ProviderSignature">
    /// Opaque, provider-specific token that must be echoed back on the
    /// follow-up request (Gemini's <c>thoughtSignature</c>). Null for
    /// providers that don't use this mechanism.
    /// </param>
    public sealed record ToolCallStart(string Id, string FunctionName, string? ProviderSignature = null) : StreamEvent;

    /// <summary>A chunk of arguments JSON for an in-progress tool call.</summary>
    /// <param name="Id">The tool call identifier this chunk belongs to.</param>
    /// <param name="ArgumentsChunk">A fragment of the JSON arguments string.</param>
    public sealed record ToolCallDelta(string Id, string ArgumentsChunk) : StreamEvent;

    /// <summary>An assistant-generated media part embedded in the response stream
    /// (e.g., Gemini image-generation models emit inline image bytes).</summary>
    /// <param name="Media">The media payload (data URI or accessible URI plus MIME type).</param>
    public sealed record MediaPart(MediaContent Media) : StreamEvent;

    /// <summary>Token usage information, typically emitted near the end of the stream.</summary>
    /// <param name="TokenUsage">The token counts.</param>
    public sealed record Usage(TokenUsage TokenUsage) : StreamEvent;

    /// <summary>Signals the stream has completed. Always the final event yielded.</summary>
    /// <param name="IsTruncated">Whether the response was truncated due to max token limits.</param>
    public sealed record StreamCompleted(bool IsTruncated) : StreamEvent;
}
