using System.Net;
using System.Text.RegularExpressions;

namespace FieldCure.AssistStudio.Core.Helpers;

/// <summary>
/// Lightweight HTML-to-text extractor with no external dependencies.
/// Strips tags, scripts, styles, and normalizes whitespace to produce readable plain text.
/// </summary>
public static partial class HtmlTextExtractor
{
    /// <summary>
    /// Extracts readable text from an HTML string by removing tags, scripts, styles,
    /// and normalizing whitespace. Returns at most <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="html">The HTML content to extract text from.</param>
    /// <param name="maxLength">Maximum length of the returned text. Default is 8000.</param>
    /// <returns>The extracted plain text, truncated if necessary.</returns>
    public static string Extract(string html, int maxLength = 8000)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var text = html;

        // 1. Remove <script> blocks
        text = ScriptRegex().Replace(text, string.Empty);

        // 2. Remove <style> blocks
        text = StyleRegex().Replace(text, string.Empty);

        // 3. Remove <head> block
        text = HeadRegex().Replace(text, string.Empty);

        // 4. Remove HTML comments
        text = CommentRegex().Replace(text, string.Empty);

        // 5. Replace block-level elements with newlines
        text = BlockElementRegex().Replace(text, "\n");

        // 6. Strip all remaining HTML tags
        text = TagRegex().Replace(text, string.Empty);

        // 7. Decode HTML entities
        text = WebUtility.HtmlDecode(text);

        // 8. Collapse consecutive whitespace on each line, then collapse blank lines
        text = HorizontalWhitespaceRegex().Replace(text, " ");
        text = BlankLinesRegex().Replace(text, "\n\n");
        text = text.Trim();

        // 9. Truncate if necessary
        if (text.Length > maxLength)
        {
            text = string.Concat(text.AsSpan(0, maxLength), "\n[Truncated]");
        }

        return text;
    }

    #region Generated Regexes

    /// <summary>Matches <c>&lt;script&gt;</c> blocks so their executable content is dropped before text extraction.</summary>
    [GeneratedRegex(@"<script[^>]*>[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRegex();

    /// <summary>Matches <c>&lt;style&gt;</c> blocks so CSS rules don't leak into the extracted prose.</summary>
    [GeneratedRegex(@"<style[^>]*>[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleRegex();

    /// <summary>Matches the document <c>&lt;head&gt;</c> block so metadata and links don't appear in the output.</summary>
    [GeneratedRegex(@"<head[^>]*>[\s\S]*?</head>", RegexOptions.IgnoreCase)]
    private static partial Regex HeadRegex();

    /// <summary>Matches HTML comments for removal.</summary>
    [GeneratedRegex(@"<!--[\s\S]*?-->")]
    private static partial Regex CommentRegex();

    /// <summary>Matches block-level opening tags that should be converted to line breaks to preserve paragraph structure.</summary>
    [GeneratedRegex(@"<\s*(?:br|p|div|li|h[1-6]|tr|blockquote|section|article|aside|dt|dd|figcaption|details|summary)\b[^>]*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockElementRegex();

    /// <summary>Matches any remaining HTML tag so only text content survives.</summary>
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    /// <summary>Matches runs of horizontal whitespace (excluding newlines) for collapsing into a single space.</summary>
    [GeneratedRegex(@"[^\S\n]+")]
    private static partial Regex HorizontalWhitespaceRegex();

    /// <summary>Matches three or more consecutive newlines, collapsed to a single blank line.</summary>
    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesRegex();

    #endregion
}
