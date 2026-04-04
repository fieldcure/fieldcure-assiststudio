namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Specifies the kind of media content for rendering decisions.
/// </summary>
public enum MediaContentKind
{
    /// <summary>Inline image (rendered as &lt;img&gt;).</summary>
    Image,

    /// <summary>Inline audio (rendered as &lt;audio&gt; player).</summary>
    Audio,

    /// <summary>Inline video (rendered as &lt;video&gt; player).</summary>
    Video,

    /// <summary>Downloadable file (non-renderable MIME type).</summary>
    Download
}

/// <summary>
/// Represents extracted media content from a tool result content block.
/// <para>
/// <see cref="MediaUri"/> may be a <c>data:</c> URI, a <c>file://</c> URI,
/// or an <c>http(s)://</c> URL depending on how the content was delivered.
/// </para>
/// </summary>
/// <param name="MediaUri">The media URI (data URI, file URI, or HTTP URL).</param>
/// <param name="MimeType">The MIME type of the content (e.g. <c>image/png</c>, <c>audio/wav</c>).</param>
/// <param name="Kind">The kind of media content for rendering decisions.</param>
public record class MediaContent(string MediaUri, string MimeType, MediaContentKind Kind);
