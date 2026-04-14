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
    /// <param name="providerName">Provider name (e.g. "OpenAI", "Claude").</param>
    /// <param name="modelId">Model identifier (e.g. "gpt-4o", "claude-sonnet-4-20250514").</param>
    /// <returns>A <see cref="MarkdownExportResult"/> containing the Markdown text and extracted media blobs.</returns>
    public static MarkdownExportResult Export(
        IReadOnlyList<ChatMessage> messages,
        string? title = null,
        string? providerName = null,
        string? modelId = null)
    {
        var sb = new StringBuilder();
        var media = new Dictionary<string, ReadOnlyMemory<byte>>();
        var mediaCounter = 0;

        // Collect tool results keyed by ToolCallId for matching.
        var toolResults = BuildToolResultMap(messages);

        // Count user-visible messages (User + Assistant only) for frontmatter.
        var visibleCount = 0;
        foreach (var m in messages)
            if (m.Role is ChatRole.User or ChatRole.Assistant) visibleCount++;

        // --- Frontmatter ---
        AppendFrontmatter(sb, title, providerName, modelId, messages, visibleCount);

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
                    // Tool results are consumed by the preceding Assistant's ToolCalls.
                    // If orphaned (no matching Assistant), emit standalone.
                    if (!toolResults.ContainsKey(msg.ToolCallId ?? ""))
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

    private static void AppendFrontmatter(
        StringBuilder sb, string? title, string? providerName, string? modelId,
        IReadOnlyList<ChatMessage> messages, int messageCount)
    {
        sb.AppendLine("---");

        if (!string.IsNullOrWhiteSpace(title))
            sb.AppendLine($"title: \"{EscapeYamlString(title)}\"");

        if (messages.Count > 0)
            sb.AppendLine($"created: {messages[0].Timestamp.ToString("O", CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrWhiteSpace(providerName))
            sb.AppendLine($"provider: {providerName}");

        if (!string.IsNullOrWhiteSpace(modelId))
            sb.AppendLine($"model: {modelId}");

        sb.AppendLine($"message_count: {messageCount}");
        sb.AppendLine("---");
        sb.AppendLine();
    }

    private static string EscapeYamlString(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    #endregion

    #region User Message

    private static void AppendUserMessage(
        StringBuilder sb, ChatMessage msg,
        Dictionary<string, ReadOnlyMemory<byte>> media, ref int mediaCounter)
    {
        sb.AppendLine("## User");
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
    }

    #endregion

    #region Assistant Message

    private static void AppendAssistantMessage(
        StringBuilder sb, ChatMessage msg,
        Dictionary<string, string> toolResults,
        Dictionary<string, ReadOnlyMemory<byte>> media, ref int mediaCounter)
    {
        // Header
        if (msg.Summary is not null)
        {
            sb.AppendLine("## Assistant (요약)");
            sb.AppendLine();
            AppendSummaryBlockquote(sb, msg.Summary);
        }
        else
        {
            sb.AppendLine("## Assistant");
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
    }

    private static void AppendSummaryBlockquote(StringBuilder sb, SummaryMeta summary)
    {
        var count = summary.CoveredMessageIds.Count;
        if (summary.CoveredTokenCount > 0)
        {
            sb.AppendLine($"> 이전 메시지 {count}개 (약 {summary.CoveredTokenCount:N0} 토큰)를 요약한 내용입니다.");
        }
        else
        {
            sb.AppendLine($"> 이전 메시지 {count}개를 요약한 내용입니다.");
        }
        sb.AppendLine();
    }

    #endregion

    #region Tool Blocks

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
