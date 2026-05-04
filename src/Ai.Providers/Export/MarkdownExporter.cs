using System.Globalization;
using System.Text;
using System.Text.Json;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Export;

/// <summary>
/// Converts an in-memory conversation (active branch) into a Markdown document.
/// Pure function — no file I/O, no async, no platform dependencies.
/// </summary>
public static class MarkdownExporter
{
    #region Public API

    /// <summary>
    /// Exports the given messages as a Markdown document with optional media extraction.
    /// </summary>
    /// <param name="messages">Active-path messages in chronological order (from <c>GetMessages()</c>).</param>
    /// <param name="title">Conversation title for the YAML frontmatter.</param>
    /// <returns>A <see cref="MarkdownExportResult"/> containing the Markdown text and extracted media blobs.</returns>
    public static MarkdownExportResult Export(
        IReadOnlyList<ChatMessage> messages,
        string? title = null)
    {
        var sb = new StringBuilder();
        var media = new Dictionary<string, ReadOnlyMemory<byte>>();
        var mediaCounter = 0;

        // Collect tool results keyed by ToolCallId for matching.
        var toolResults = BuildToolResultMap(messages);

        // Track which ToolCallIds are claimed by an Assistant's ToolCalls.
        var claimedToolCallIds = new HashSet<string>();
        foreach (var m in messages)
        {
            if (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 })
                foreach (var tc in m.ToolCalls)
                    claimedToolCallIds.Add(tc.Id);
        }

        // Count user-visible messages (User + Assistant only) for frontmatter.
        var visibleCount = 0;
        foreach (var m in messages)
            if (m.Role is ChatRole.User or ChatRole.Assistant) visibleCount++;

        // --- Frontmatter ---
        AppendFrontmatter(sb, title, messages, visibleCount);

        // --- Messages ---
        foreach (var msg in messages)
        {
            switch (msg.Role)
            {
                case ChatRole.System:
                    // System prompt history is not persisted; skip.
                    break;

                case ChatRole.User:
                    AppendUserMessage(sb, msg, media, ref mediaCounter);
                    break;

                case ChatRole.Assistant:
                    AppendAssistantMessage(sb, msg, toolResults, media, ref mediaCounter);
                    break;

                case ChatRole.Tool:
                    // Tool results claimed by a preceding Assistant's ToolCalls are already
                    // rendered inside that Assistant's <details> block — skip them here.
                    // Only orphaned Tool messages (no matching ToolCall) get standalone output.
                    if (!claimedToolCallIds.Contains(msg.ToolCallId ?? ""))
                        AppendOrphanToolResult(sb, msg);
                    break;
            }
        }

        return new MarkdownExportResult
        {
            Markdown = sb.ToString(),
            Media = media
        };
    }

    #endregion

    #region Frontmatter

    /// <summary>Writes the YAML frontmatter header (title, created timestamp, message count) to the buffer.</summary>
    private static void AppendFrontmatter(
        StringBuilder sb, string? title,
        IReadOnlyList<ChatMessage> messages, int messageCount)
    {
        sb.AppendLine("---");

        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine($"title: \"{EscapeYamlString(title)}\"");

        if (messages.Count > 0)
            sb.AppendLine($"created: {messages[0].Timestamp.ToString("O", CultureInfo.InvariantCulture)}");

        sb.AppendLine($"message_count: {messageCount}");
        sb.AppendLine("---");
        sb.AppendLine();
    }

    /// <summary>Escapes backslashes and double quotes for safe embedding inside a YAML double-quoted string.</summary>
    private static string EscapeYamlString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    #endregion

    #region User Message

    /// <summary>Renders a user message as a collapsible details block, including attachments and text content.</summary>
    private static void AppendUserMessage(
        StringBuilder sb, ChatMessage msg,
        Dictionary<string, ReadOnlyMemory<byte>> media, ref int mediaCounter)
    {
        sb.AppendLine("<details open>");
        sb.AppendLine("<summary><b>User</b></summary>");
        sb.AppendLine();

        // Attachments before content.
        if (msg.Attachments.Count > 0)
        {
            foreach (var att in msg.Attachments)
                AppendAttachment(sb, att, media, ref mediaCounter);
        }

        if (!string.IsNullOrEmpty(msg.Content))
        {
            sb.AppendLine(msg.Content);
            sb.AppendLine();
        }

        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    #endregion

    #region Assistant Message

    /// <summary>Renders an assistant message with thinking, content, tool calls, and attached tool media.</summary>
    private static void AppendAssistantMessage(
        StringBuilder sb, ChatMessage msg,
        Dictionary<string, string> toolResults,
        Dictionary<string, ReadOnlyMemory<byte>> media, ref int mediaCounter)
    {
        // Header
        var attribution = BuildAttribution(msg);
        if (msg.Summary is not null)
        {
            sb.AppendLine("<details open>");
            sb.AppendLine($"<summary><b>Assistant</b> (summary)</summary>");
            sb.AppendLine();
            AppendSummaryBlockquote(sb, msg.Summary);
        }
        else
        {
            sb.AppendLine("<details open>");
            sb.AppendLine($"<summary><b>Assistant</b>{attribution}</summary>");
            sb.AppendLine();
        }

        // Thinking content (collapsible)
        if (!string.IsNullOrEmpty(msg.ThinkingContent))
        {
            sb.AppendLine("<details>");
            sb.AppendLine("<summary>Thinking</summary>");
            sb.AppendLine();
            sb.AppendLine(msg.ThinkingContent);
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        // Text content
        if (!string.IsNullOrEmpty(msg.Content))
        {
            sb.AppendLine(msg.Content);
            sb.AppendLine();
        }

        // Tool calls
        if (msg.ToolCalls is { Count: > 0 })
        {
            foreach (var tc in msg.ToolCalls)
            {
                AppendToolCallBlock(sb, tc, toolResults);
            }
        }

        // Tool media (from tool execution results stored on this message)
        if (msg.ToolMedia is { Count: > 0 })
        {
            foreach (var tm in msg.ToolMedia)
                AppendToolMedia(sb, tm, media, ref mediaCounter);
        }

        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    /// <summary>Writes a blockquote describing how many prior messages this summary replaces.</summary>
    private static void AppendSummaryBlockquote(StringBuilder sb, SummaryMeta summary)
    {
        var count = summary.CoveredMessageIds.Count;
        if (summary.CoveredTokenCount > 0)
        {
            sb.AppendLine($"> Summary of the previous {count} message(s) (~{summary.CoveredTokenCount:N0} tokens).");
        }
        else
        {
            sb.AppendLine($"> Summary of the previous {count} message(s).");
        }
        sb.AppendLine();
    }

    #endregion

    #region Tool Blocks

    /// <summary>Builds a lookup of tool result content keyed by <see cref="ChatMessage.ToolCallId"/>.</summary>
    private static Dictionary<string, string> BuildToolResultMap(IReadOnlyList<ChatMessage> messages)
    {
        var map = new Dictionary<string, string>();
        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.Tool && msg.ToolCallId is not null)
                map[msg.ToolCallId] = msg.Content;
        }
        return map;
    }

    /// <summary>Renders a single tool call as a collapsible block with input JSON and matched result text.</summary>
    private static void AppendToolCallBlock(
        StringBuilder sb, ToolCall tc, Dictionary<string, string> toolResults)
    {
        sb.AppendLine("<details>");
        sb.AppendLine($"<summary>Tool: {tc.FunctionName}</summary>");
        sb.AppendLine();

        // Input
        sb.AppendLine("**Input:**");
        sb.AppendLine("```json");
        sb.AppendLine(TryPrettyPrintJson(tc.Arguments));
        sb.AppendLine("```");
        sb.AppendLine();

        // Result
        if (toolResults.TryGetValue(tc.Id, out var result))
        {
            sb.AppendLine("**Result:**");
            sb.AppendLine("```");
            sb.AppendLine(result);
            sb.AppendLine("```");
            sb.AppendLine();

            // Mark as consumed so it won't be emitted as orphan.
            toolResults.Remove(tc.Id);
        }

        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    /// <summary>Renders a tool result message that has no matching assistant tool call.</summary>
    private static void AppendOrphanToolResult(StringBuilder sb, ChatMessage msg)
    {
        sb.AppendLine("<details>");
        sb.AppendLine("<summary>Tool Result</summary>");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(msg.Content);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    #endregion

    #region Attachments

    /// <summary>Renders an attachment inline; images are extracted into the media dictionary with a relative reference.</summary>
    private static void AppendAttachment(
        StringBuilder sb, ChatAttachment att,
        Dictionary<string, ReadOnlyMemory<byte>> media, ref int mediaCounter)
    {
        switch (att.Type)
        {
            case AttachmentType.Image:
                if (att.Data.Length > 0)
                {
                    var ext = MimeToExtension(att.MimeType);
                    var key = $"media/img_{++mediaCounter:D3}{ext}";
                    media[key] = att.Data;
                    sb.AppendLine($"![{att.FileName}]({key})");
                    sb.AppendLine();
                }
                break;

            case AttachmentType.TextFile:
                sb.AppendLine($"**{att.FileName}:**");
                sb.AppendLine("```");
                sb.AppendLine(System.Text.Encoding.UTF8.GetString(att.Data));
                sb.AppendLine("```");
                sb.AppendLine();
                break;

            case AttachmentType.Document:
                sb.AppendLine($"📎 {att.FileName}");
                sb.AppendLine();
                break;
        }
    }

    #endregion

    #region Tool Media

    /// <summary>Renders tool-produced media: decodes data URIs into blobs, preserves external URLs inline.</summary>
    private static void AppendToolMedia(
        StringBuilder sb, MediaContent tm,
        Dictionary<string, ReadOnlyMemory<byte>> media, ref int mediaCounter)
    {
        if (tm.MediaUri.StartsWith("data:", StringComparison.Ordinal))
        {
            // Decode data: URI → extract bytes.
            var bytes = DecodeDataUri(tm.MediaUri);
            if (bytes is not null)
            {
                var ext = MimeToExtension(tm.MimeType);
                var key = $"media/img_{++mediaCounter:D3}{ext}";
                media[key] = bytes;
                sb.AppendLine($"![media]({key})");
                sb.AppendLine();
            }
        }
        else
        {
            // http(s):// or file:// → preserve as-is.
            sb.AppendLine($"![media]({tm.MediaUri})");
            sb.AppendLine();
        }
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Builds " · ProviderName · ModelId" suffix for assistant headers.
    /// Returns empty string when both are null.
    /// </summary>
    private static string BuildAttribution(ChatMessage msg)
    {
        if (msg.ProviderName is null && msg.ProviderModelId is null)
            return "";

        var parts = new List<string>(2);
        if (msg.ProviderName is not null) parts.Add(msg.ProviderName);
        if (msg.ProviderModelId is not null) parts.Add(msg.ProviderModelId);

        return " · " + string.Join(" · ", parts);
    }

    /// <summary>Maps a MIME type to a conventional file extension; unknown types fall back to <c>.bin</c>.</summary>
    private static string MimeToExtension(string? mimeType)
    {
        return mimeType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            "image/bmp" => ".bmp",
            "audio/wav" or "audio/wave" => ".wav",
            "audio/mp3" or "audio/mpeg" => ".mp3",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "application/pdf" => ".pdf",
            _ => ".bin"
        };
    }

    /// <summary>Decodes the base64 payload of a <c>data:</c> URI; returns <c>null</c> on malformed input.</summary>
    private static byte[]? DecodeDataUri(string dataUri)
    {
        // Format: data:{mime};base64,{data}
        var commaIdx = dataUri.IndexOf(',');
        if (commaIdx < 0) return null;

        var base64 = dataUri[(commaIdx + 1)..];
        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Pretty-prints a JSON string; returns the input unchanged if parsing fails.</summary>
    private static string TryPrettyPrintJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }

    #endregion
}
