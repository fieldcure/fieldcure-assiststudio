using System.Text;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Helpers;

/// <summary>
/// Builds labeled segments for user messages with attachments,
/// producing a provider-agnostic layout that each provider translates
/// into its native content block format.
/// </summary>
/// <remarks>
/// <para>
/// Single attachment: labels without a number
/// (<c>[Attachment — type: name]</c>).
/// </para>
/// <para>
/// Two or more attachments: numbered labels
/// (<c>[Attachment 1 — type: name]</c>).
/// </para>
/// <para>
/// No attachments: returns <c>null</c> — providers use their existing path.
/// </para>
/// </remarks>
public static class AttachmentLabelBuilder
{
    /// <summary>
    /// Produces a labeled layout for the message's attachments.
    /// Returns <c>null</c> for attachment-free messages,
    /// signaling that providers should use their existing path.
    /// </summary>
    public static LabeledMessageLayout? Build(
        string userText,
        IReadOnlyList<ChatAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return null;

        var numbered = attachments.Count >= 2;
        var binarySegments = new List<LabeledBinarySegment>();
        var textSegments = new List<LabeledTextSegment>();

        for (int i = 0; i < attachments.Count; i++)
        {
            var att = attachments[i];
            var label = BuildLabel(att, numbered ? i + 1 : null);

            if (att.Type is AttachmentType.Image or AttachmentType.Document or AttachmentType.Audio)
            {
                binarySegments.Add(new LabeledBinarySegment(label, att));
            }
            else
            {
                var content = Encoding.UTF8.GetString(att.Data);
                textSegments.Add(new LabeledTextSegment(label, content));
            }
        }

        var userBlock = BuildUserTextBlock(userText, textSegments);
        return new LabeledMessageLayout(binarySegments, userBlock);
    }

    /// <summary>
    /// Builds a single attachment label string.
    /// </summary>
    private static string BuildLabel(ChatAttachment att, int? number)
    {
        var (type, name) = att.Type switch
        {
            AttachmentType.Image => ("image", att.FileName),
            AttachmentType.Document => ("document", att.FileName),
            AttachmentType.Audio => ("audio", att.FileName),
            AttachmentType.TextFile when att.Source == AttachmentSource.Pasted =>
                ("pasted text", $"{att.CharCount:N0} chars, {att.LineCount:N0} lines"),
            AttachmentType.TextFile => ("file", att.FileName),
            _ => ("attachment", att.FileName ?? "unknown")
        };

        return number.HasValue
            ? $"[Attachment {number.Value} \u2014 {type}: {name}]"
            : $"[Attachment \u2014 {type}: {name}]";
    }

    /// <summary>
    /// Composes the user text block with [User message] header
    /// and all labeled text attachments appended in order.
    /// </summary>
    private static string BuildUserTextBlock(
        string userText,
        IReadOnlyList<LabeledTextSegment> textSegments)
    {
        var sb = new StringBuilder();
        sb.Append("[User message]\n");
        sb.Append(userText ?? string.Empty);

        foreach (var seg in textSegments)
        {
            sb.Append("\n\n");
            sb.Append(seg.Label);
            sb.Append('\n');
            sb.Append(seg.Content);
        }

        return sb.ToString();
    }
}

/// <summary>
/// Provider-agnostic layout for a labeled message with attachments.
/// </summary>
public sealed record LabeledMessageLayout(
    IReadOnlyList<LabeledBinarySegment> BinarySegments,
    string UserTextBlock);

/// <summary>
/// Image or document attachment with its preceding text label.
/// </summary>
public sealed record LabeledBinarySegment(string Label, ChatAttachment Attachment);

/// <summary>
/// Text attachment content with its label (for inlining into the user text block).
/// </summary>
public sealed record LabeledTextSegment(string Label, string Content);
