namespace FieldCure.AssistStudio.Controls.Helpers;

/// <summary>
/// Shared utilities for text attachment processing.
/// </summary>
internal static class TextAttachmentHelper
{
    /// <summary>
    /// Slices UTF-8 bytes at a safe boundary, never in the middle of a multi-byte sequence.
    /// Prevents replacement characters at byte boundaries for Korean, CJK, or emoji text.
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
}
