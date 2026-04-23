using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Helpers;

/// <summary>
/// Streaming parser that separates &lt;think&gt;...&lt;/think&gt; blocks from regular content.
/// Handles tag boundaries that span across multiple streaming chunks.
/// Outputs <see cref="StreamEvent.ThinkingDelta"/> for thinking content
/// and <see cref="StreamEvent.TextDelta"/> for regular content.
/// </summary>
internal sealed class ThinkTagParser
{
    /// <summary>The opening marker that starts a thinking block.</summary>
    private const string OpenTag = "<think>";

    /// <summary>The closing marker that ends a thinking block.</summary>
    private const string CloseTag = "</think>";

    /// <summary>Tracks whether the parser is currently inside a thinking block.</summary>
    private bool _insideThink;

    /// <summary>Carry-over text from previous feeds that may contain a partial tag.</summary>
    private string _buffer = "";

    /// <summary>
    /// Feeds a chunk of text and yields the appropriate stream events.
    /// </summary>
    public IEnumerable<StreamEvent> Feed(string chunk)
    {
        _buffer += chunk;

        while (_buffer.Length > 0)
        {
            if (_insideThink)
            {
                var closeIdx = _buffer.IndexOf(CloseTag, StringComparison.Ordinal);
                if (closeIdx >= 0)
                {
                    // Emit thinking content before the close tag
                    var thinking = _buffer[..closeIdx];
                    if (thinking.Length > 0)
                        yield return new StreamEvent.ThinkingDelta(thinking);

                    _buffer = _buffer[(closeIdx + CloseTag.Length)..];
                    _insideThink = false;

                    // Skip leading whitespace after </think>
                    _buffer = _buffer.TrimStart('\n', '\r');
                }
                else
                {
                    // Close tag might be partially at the end of buffer
                    var safeLen = _buffer.Length - (CloseTag.Length - 1);
                    if (safeLen > 0)
                    {
                        yield return new StreamEvent.ThinkingDelta(_buffer[..safeLen]);
                        _buffer = _buffer[safeLen..];
                    }
                    break; // Wait for more data
                }
            }
            else
            {
                var openIdx = _buffer.IndexOf(OpenTag, StringComparison.Ordinal);
                if (openIdx >= 0)
                {
                    // Emit text content before the open tag
                    var text = _buffer[..openIdx];
                    if (text.Length > 0)
                        yield return new StreamEvent.TextDelta(text);

                    _buffer = _buffer[(openIdx + OpenTag.Length)..];
                    _insideThink = true;

                    // Skip leading whitespace after <think>
                    _buffer = _buffer.TrimStart('\n', '\r');
                }
                else
                {
                    // Open tag might be partially at the end of buffer (e.g., "<thi")
                    var partialIdx = FindPartialTagStart(_buffer, OpenTag);
                    if (partialIdx >= 0 && partialIdx < _buffer.Length)
                    {
                        var text = _buffer[..partialIdx];
                        if (text.Length > 0)
                            yield return new StreamEvent.TextDelta(text);
                        _buffer = _buffer[partialIdx..];
                    }
                    else
                    {
                        // No tag found, emit all as text
                        yield return new StreamEvent.TextDelta(_buffer);
                        _buffer = "";
                    }
                    break; // Wait for more data
                }
            }
        }
    }

    /// <summary>
    /// Finds the start index of a partial tag match at the end of the text.
    /// Returns -1 if no partial match is found.
    /// </summary>
    private static int FindPartialTagStart(string text, string tag)
    {
        // Check if the text ends with a prefix of the tag (e.g., "<", "<t", "<th", etc.)
        var maxCheck = Math.Min(tag.Length - 1, text.Length);
        for (var len = maxCheck; len >= 1; len--)
        {
            if (text.AsSpan(text.Length - len).SequenceEqual(tag.AsSpan(0, len)))
                return text.Length - len;
        }
        return -1;
    }
}
