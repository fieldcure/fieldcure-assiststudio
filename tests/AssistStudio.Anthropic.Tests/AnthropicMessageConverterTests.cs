using Anthropic.Models.Messages;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Anthropic.Tests;

/// <summary>
/// Tests conversion between AssistStudio messages and Anthropic SDK message structures.
/// </summary>
[TestClass]
public class AnthropicMessageConverterTests
{
    [TestMethod]
    public void SystemMessages_ExtractedToSystemPrompt()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.System, "Be concise."),
            new(ChatRole.User, "Hello"),
        };

        var result = AnthropicMessageConverter.Convert(messages);

        Assert.AreEqual("You are a helpful assistant.\n\nBe concise.", result.SystemPrompt);
        Assert.AreEqual(1, result.Messages.Count); // Only the user message
    }

    [TestMethod]
    public void UserMessage_TextOnly_ConvertedToStringContent()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello, Claude!"),
        };

        var result = AnthropicMessageConverter.Convert(messages);

        Assert.AreEqual(1, result.Messages.Count);
        var msg = result.Messages[0];
        Assert.AreEqual(Role.User, (Role)msg.Role);
        Assert.IsTrue(msg.Content.TryPickString(out var text));
        Assert.AreEqual("Hello, Claude!", text);
    }

    [TestMethod]
    public void AssistantMessage_TextOnly_ConvertedToStringContent()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "I can help with that."),
        };

        var result = AnthropicMessageConverter.Convert(messages);

        Assert.AreEqual(1, result.Messages.Count);
        var msg = result.Messages[0];
        Assert.AreEqual(Role.Assistant, (Role)msg.Role);
        Assert.IsTrue(msg.Content.TryPickString(out var text));
        Assert.AreEqual("I can help with that.", text);
    }

    [TestMethod]
    public void AssistantMessage_WithThinking_DropsThinkingKeepsText()
    {
        // Thinking content is dropped because ChatMessage does not preserve the signature.
        // Sending a blank signature causes 422 on multi-turn extended thinking conversations.
        var msg = new ChatMessage(ChatRole.Assistant, "The answer is 42.")
        {
            ThinkingContent = "Let me work this out...",
        };
        var messages = new List<ChatMessage> { msg };

        var result = AnthropicMessageConverter.Convert(messages);

        Assert.AreEqual(1, result.Messages.Count);
        var converted = result.Messages[0];
        Assert.IsTrue(converted.Content.TryPickString(out var text));
        Assert.AreEqual("The answer is 42.", text);
    }

    [TestMethod]
    public void NoSystemMessages_SystemPromptIsNull()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hi"),
            new(ChatRole.Assistant, "Hello!"),
        };

        var result = AnthropicMessageConverter.Convert(messages);

        Assert.IsNull(result.SystemPrompt);
        Assert.AreEqual(2, result.Messages.Count);
    }

    [TestMethod]
    public void ToolMessages_SkippedSilently()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "Search for cats"),
            new(ChatRole.Assistant, "I'll search."),
            new(ChatRole.Tool, "Results: 3 cats found") { ToolCallId = "call_1" },
            new(ChatRole.Assistant, "Found 3 cats!"),
        };

        var result = AnthropicMessageConverter.Convert(messages);

        // Tool message should be skipped
        Assert.AreEqual(3, result.Messages.Count);
        Assert.AreEqual(Role.User, (Role)result.Messages[0].Role);
        Assert.AreEqual(Role.Assistant, (Role)result.Messages[1].Role);
        Assert.AreEqual(Role.Assistant, (Role)result.Messages[2].Role);
    }
}
