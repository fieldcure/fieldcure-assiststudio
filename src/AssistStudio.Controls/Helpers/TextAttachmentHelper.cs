using System.Text;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Shared utilities for text attachment preview rendering, used by both
/// <see cref="AttachmentPreviewBar"/> and WebViewChatRenderer.
/// </summary>
internal static class TextAttachmentHelper
{
    /// <summary>
    /// Slices UTF-8 bytes at a safe boundary, never in the middle of a multi-byte sequence.
    /// Prevents replacement characters (&#xFFFD;) when truncating Korean, CJK, or emoji text.
    /// </summary>
    public static ReadOnlySpan<byte> SafeUtf8Slice(ReadOnlySpan<byte> data, int maxBytes)
    {
        if (data.Length <= maxBytes) return data;

        // UTF-8 continuation bytes are 10xxxxxx — walk back to the start of a char
        int cut = maxBytes;
        while (cut > 0 && (data[cut] & 0xC0) == 0x80)
            cut--;
        return data[..cut];
    }

    /// <summary>
    /// Generates a preview string from the attachment's raw bytes,
    /// truncated to the first 2 KB or 20 lines, whichever comes first.
    /// </summary>
    public static string BuildPreviewText(ChatAttachment attachment)
    {
        const int MaxBytes = 2048;
        const int MaxLines = 20;

        var slice = SafeUtf8Slice(attachment.Data, MaxBytes);
        var text = Encoding.UTF8.GetString(slice);

        int lineBreaks = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' && ++lineBreaks >= MaxLines)
                return text[..i];
        }

        return text;
    }

    /// <summary>
    /// Returns the badge label shown on a text attachment preview card
    /// (e.g., "PASTED", "TXT", "MD", "LOG").
    /// </summary>
    public static string GetBadgeLabel(ChatAttachment attachment)
    {
        if (attachment.Source == AttachmentSource.Pasted)
            return "PASTED";

        var ext = Path.GetExtension(attachment.FileName).TrimStart('.').ToUpperInvariant();
        return string.IsNullOrEmpty(ext) ? "TEXT" : ext;
    }
}
