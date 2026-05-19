using System.Text.Json;
using Anthropic.Models.Messages;
using FieldCure.Ai.Providers.Models;
using Role = Anthropic.Models.Messages.Role;

namespace FieldCure.AssistStudio.Anthropic;

/// <summary>
/// Converts ChatPanel <see cref="ChatMessage"/> instances to Anthropic SDK <see cref="MessageParam"/> instances.
/// This conversion is lossy — Controls-specific metadata (branching, summary, provider info, elapsed time) is dropped.
/// </summary>
public static class AnthropicMessageConverter
{
    /// <summary>
    /// Converts a list of ChatPanel messages to Anthropic SDK message format.
    /// </summary>
    /// <param name="messages">The conversation messages (e.g., from ChatPanel.GetConversationSnapshot).</param>
    /// <returns>A result containing the converted messages and extracted system prompt.</returns>
    public static ConversionResult Convert(IReadOnlyList<ChatMessage> messages)
    {
        var systemParts = new List<string>();
        var result = new List<MessageParam>();

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];
            switch (msg.Role)
            {
                case ChatRole.System:
                    if (!string.IsNullOrEmpty(msg.Content))
                        systemParts.Add(msg.Content);
                    break;

                case ChatRole.User:
                    result.Add(ConvertUserMessage(msg));
                    break;

                case ChatRole.Assistant:
                    result.Add(ConvertAssistantMessage(msg));
                    break;

                case ChatRole.Tool:
                    // Anthropic requires consecutive tool results to be combined into a
                    // single user message whose content is a list of ToolResultBlockParams.
                    // Sending each tool result as its own user message produces a 422.
                    var blocks = new List<ContentBlockParam>();
                    while (i < messages.Count && messages[i].Role == ChatRole.Tool)
                    {
                        blocks.Add(BuildToolResultBlock(messages[i]));
                        i++;
                    }
                    i--; // step back so the for-loop's i++ lands on the next non-tool message
                    result.Add(new MessageParam
                    {
                        Role = Role.User,
                        Content = new MessageParamContent(blocks),
                    });
                    break;
            }
        }

        var systemPrompt = systemParts.Count > 0
            ? string.Join("\n\n", systemParts)
            : null;

        return new ConversionResult
        {
            Messages = result,
            SystemPrompt = systemPrompt,
        };
    }

    /// <summary>Converts a <see cref="ChatRole.User"/> message to an Anthropic <see cref="MessageParam"/>.</summary>
    private static MessageParam ConvertUserMessage(ChatMessage msg)
    {
        // Simple case: text only, no attachments
        if (msg.Attachments is not { Count: > 0 })
        {
            return new MessageParam
            {
                Role = Role.User,
                Content = new MessageParamContent(msg.Content ?? ""),
            };
        }

        // With attachments: build content block list
        var blocks = new List<ContentBlockParam>();

        foreach (var att in msg.Attachments)
        {
            switch (att.Type)
            {
                case AttachmentType.Image when att.Data is { Length: > 0 }:
                    blocks.Add(new ImageBlockParam
                    {
                        Source = new Base64ImageSource
                        {
                            Data = System.Convert.ToBase64String(att.Data),
                            MediaType = MapImageMediaType(att.MimeType),
                        },
                    });
                    break;

                case AttachmentType.TextFile when att.Data is { Length: > 0 }:
                    // Inline text file content as a text block
                    var textContent = System.Text.Encoding.UTF8.GetString(att.Data);
                    blocks.Add(new TextBlockParam { Text = $"[{att.FileName}]\n{textContent}" });
                    break;
            }
        }

        if (!string.IsNullOrEmpty(msg.Content))
            blocks.Add(new TextBlockParam { Text = msg.Content });

        return new MessageParam
        {
            Role = Role.User,
            Content = blocks.Count > 0
                ? new MessageParamContent(blocks)
                : new MessageParamContent(msg.Content ?? ""),
        };
    }

    /// <summary>Converts a <see cref="ChatRole.Assistant"/> message to an Anthropic <see cref="MessageParam"/>.</summary>
    /// <remarks>
    /// Drops thinking blocks when reconstructing assistant messages for the API.
    /// This is correct for text-only and tool-only multi-turn — Anthropic accepts
    /// assistant messages without thinking blocks even when extended thinking is
    /// enabled, as long as no <c>tool_use</c> block is present *immediately after*
    /// a thinking block. When tool_use + thinking are combined in the same turn
    /// (Phase B), Anthropic requires the preceding thinking block (with its
    /// original signature) to be included; dropping it causes a 422. Implement
    /// signature round-trip per ADR-### before relaxing this drop.
    /// </remarks>
    private static MessageParam ConvertAssistantMessage(ChatMessage msg)
    {
        var hasToolCalls = msg.ToolCalls is { Count: > 0 };

        if (!hasToolCalls)
        {
            // Text-only content — use simple string representation
            return new MessageParam
            {
                Role = Role.Assistant,
                Content = new MessageParamContent(msg.Content ?? ""),
            };
        }

        // Tool-use turn: emit [TextBlockParam?, ToolUseBlockParam, ...]. Anthropic
        // permits a leading text block (the model's reasoning prelude) followed by
        // one or more tool_use blocks in the same assistant message.
        var blocks = new List<ContentBlockParam>();

        if (!string.IsNullOrEmpty(msg.Content))
            blocks.Add(new TextBlockParam { Text = msg.Content });

        foreach (var call in msg.ToolCalls!)
        {
            blocks.Add(new ToolUseBlockParam
            {
                ID = call.Id,
                Name = call.FunctionName,
                Input = ParseToolInput(call.Arguments),
            });
        }

        return new MessageParam
        {
            Role = Role.Assistant,
            Content = new MessageParamContent(blocks),
        };
    }

    /// <summary>
    /// Builds a <see cref="ToolResultBlockParam"/> from a <see cref="ChatRole.Tool"/>
    /// message. Anthropic expects each tool_use to be answered by a tool_result block
    /// with a matching <c>ToolUseId</c>; the content is forwarded as plain text.
    /// </summary>
    private static ToolResultBlockParam BuildToolResultBlock(ChatMessage msg)
    {
        return new ToolResultBlockParam
        {
            ToolUseID = msg.ToolCallId ?? "",
            Content = new ToolResultBlockParamContent(msg.Content ?? ""),
        };
    }

    /// <summary>
    /// Parses a tool call's arguments JSON string into the dictionary representation
    /// that Anthropic's <see cref="ToolUseBlockParam.Input"/> expects.
    /// </summary>
    private static IReadOnlyDictionary<string, JsonElement> ParseToolInput(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return new Dictionary<string, JsonElement>();

        using var doc = JsonDocument.Parse(argumentsJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, JsonElement>();

        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return dict;
    }

    /// <summary>Maps a MIME type string to the Anthropic SDK <see cref="MediaType"/> enum.</summary>
    private static MediaType MapImageMediaType(string? mimeType)
        => mimeType?.ToLowerInvariant() switch
        {
            "image/png" => MediaType.ImagePng,
            "image/gif" => MediaType.ImageGif,
            "image/webp" => MediaType.ImageWebP,
            _ => MediaType.ImageJpeg, // Default to JPEG
        };
}

/// <summary>
/// Result of converting ChatPanel messages to Anthropic SDK format.
/// </summary>
public sealed class ConversionResult
{
    /// <summary>The converted messages suitable for <c>MessageCreateParams.Messages</c>.</summary>
    public required IReadOnlyList<MessageParam> Messages { get; init; }

    /// <summary>The extracted system prompt, or null if no system messages were present.</summary>
    public string? SystemPrompt { get; init; }
}
