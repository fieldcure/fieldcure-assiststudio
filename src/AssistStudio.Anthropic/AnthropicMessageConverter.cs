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

        foreach (var msg in messages)
        {
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
                    // Tool messages are out of scope for text-only integration.
                    // Skip silently — callers using tool calling should extend this converter.
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
    /// Thinking content is intentionally dropped because <see cref="ChatMessage"/> does not preserve
    /// the original thinking block signature. Sending a blank signature would cause a 422 error
    /// on multi-turn conversations with extended thinking enabled.
    /// TODO: Signature preservation requires adding a ThinkingSignature field to ChatMessage.
    /// </remarks>
    private static MessageParam ConvertAssistantMessage(ChatMessage msg)
    {
        // Text-only content — use simple string representation
        return new MessageParam
        {
            Role = Role.Assistant,
            Content = new MessageParamContent(msg.Content ?? ""),
        };
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
