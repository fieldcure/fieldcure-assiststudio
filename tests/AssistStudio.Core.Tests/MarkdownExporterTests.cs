using FieldCure.Ai.Providers.Export;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests Markdown export generation for chat transcripts, tool calls, summaries, and attachments.
/// </summary>
[TestClass]
public class MarkdownExporterTests
{
    #region Helpers

    private static ChatMessage User(string content, IReadOnlyList<ChatAttachment>? attachments = null)
        => new(ChatRole.User, content) { Attachments = attachments ?? [] };

    private static ChatMessage Assistant(string content,
        IReadOnlyList<ToolCall>? toolCalls = null,
        SummaryMeta? summary = null,
        string? thinkingContent = null,
        IReadOnlyList<MediaContent>? toolMedia = null,
        string? providerName = null,
        string? providerModelId = null)
        => new(ChatRole.Assistant, content)
        {
            ToolCalls = toolCalls,
            Summary = summary,
            ThinkingContent = thinkingContent,
            ToolMedia = toolMedia,
            ProviderName = providerName,
            ProviderModelId = providerModelId,
        };

    private static ChatMessage Tool(string toolCallId, string content)
        => new(ChatRole.Tool, content) { ToolCallId = toolCallId };

    private static ChatMessage SystemMsg(string content)
        => new(ChatRole.System, content);

    #endregion

    [TestMethod]
    public void Export_EmptyConversation_ReturnsFrontmatterOnly()
    {
        var result = MarkdownExporter.Export([], title: "Empty");

        StringAssert.Contains(result.Markdown, "title: \"Empty\"");
        StringAssert.Contains(result.Markdown, "message_count: 0");
        Assert.AreEqual(0, result.Media.Count);
    }

    [TestMethod]
    public void Export_SimpleUserAssistant_CorrectHeaders()
    {
        var messages = new List<ChatMessage>
        {
            User("Hello"),
            Assistant("Hi there!"),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "<summary><b>User</b></summary>");
        StringAssert.Contains(result.Markdown, "<summary><b>Assistant</b></summary>");
        StringAssert.Contains(result.Markdown, "message_count: 2");
    }

    [TestMethod]
    public void Export_ContentPreservedVerbatim()
    {
        var markdownContent = "# Title\n\n- item 1\n- item 2\n\n```python\nprint('hello')\n```";
        var messages = new List<ChatMessage>
        {
            User("Question"),
            Assistant(markdownContent),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, markdownContent);
    }

    [TestMethod]
    public void Export_SummaryMessage_MarkedAsYoyak()
    {
        var summary = new SummaryMeta
        {
            CoveredMessageIds = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l"],
            CoveredTokenCount = 3450
        };
        var messages = new List<ChatMessage>
        {
            Assistant("This is a summary of previous messages.", summary: summary),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "<summary><b>Assistant</b> (요약)</summary>");
        StringAssert.Contains(result.Markdown, "이전 메시지 12개 (약 3,450 토큰)를 요약한 내용입니다.");
    }

    [TestMethod]
    public void Export_SummaryMessage_NoTokenCount_ShowsCountOnly()
    {
        var summary = new SummaryMeta
        {
            CoveredMessageIds = ["a", "b", "c"],
            CoveredTokenCount = 0
        };
        var messages = new List<ChatMessage>
        {
            Assistant("Summary content.", summary: summary),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "이전 메시지 3개를 요약한 내용입니다.");
        Assert.IsFalse(result.Markdown.Contains("토큰"));
    }

    [TestMethod]
    public void Export_ToolCallWithResult_DetailsBlock()
    {
        var tc = new ToolCall
        {
            Id = "tc_1",
            FunctionName = "web_search",
            Arguments = "{\"query\":\"SK hynix stock\"}"
        };
        var messages = new List<ChatMessage>
        {
            User("Search for stock info"),
            Assistant("Let me search for that.", toolCalls: [tc]),
            Tool("tc_1", "Found 5 results about SK hynix."),
            Assistant("Here are the results."),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "<summary>Tool: web_search</summary>");
        StringAssert.Contains(result.Markdown, "**Input:**");
        StringAssert.Contains(result.Markdown, "\"query\"");
        StringAssert.Contains(result.Markdown, "**Result:**");
        StringAssert.Contains(result.Markdown, "Found 5 results about SK hynix.");
    }

    [TestMethod]
    public void Export_ImageAttachment_MediaDict()
    {
        var imageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG header
        var att = new ChatAttachment("photo.png", AttachmentType.Image, imageBytes, "image/png");
        var messages = new List<ChatMessage>
        {
            User("Look at this image", attachments: [att]),
        };

        var result = MarkdownExporter.Export(messages);

        Assert.AreEqual(1, result.Media.Count);
        Assert.IsTrue(result.Media.ContainsKey("media/img_001.png"));
        CollectionAssert.AreEqual(imageBytes, result.Media["media/img_001.png"].ToArray());
        StringAssert.Contains(result.Markdown, "![photo.png](media/img_001.png)");
    }

    [TestMethod]
    public void Export_TextAttachment_InlineCodeBlock()
    {
        var textBytes = System.Text.Encoding.UTF8.GetBytes("console.log('hello');");
        var att = new ChatAttachment("script.js", AttachmentType.TextFile, textBytes, "text/plain");
        var messages = new List<ChatMessage>
        {
            User("Check this file", attachments: [att]),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "**script.js:**");
        StringAssert.Contains(result.Markdown, "console.log('hello');");
        Assert.AreEqual(0, result.Media.Count);
    }

    [TestMethod]
    public void Export_DataUriToolMedia_ExtractedToMedia()
    {
        var rawBytes = new byte[] { 0x01, 0x02, 0x03 };
        var base64 = Convert.ToBase64String(rawBytes);
        var dataUri = $"data:image/png;base64,{base64}";
        var tm = new MediaContent(dataUri, "image/png", MediaContentKind.Image);
        var messages = new List<ChatMessage>
        {
            Assistant("Here is the image.", toolMedia: [tm]),
        };

        var result = MarkdownExporter.Export(messages);

        Assert.AreEqual(1, result.Media.Count);
        Assert.IsTrue(result.Media.ContainsKey("media/img_001.png"));
        CollectionAssert.AreEqual(rawBytes, result.Media["media/img_001.png"].ToArray());
        StringAssert.Contains(result.Markdown, "![media](media/img_001.png)");
    }

    [TestMethod]
    public void Export_HttpUrlToolMedia_PreservedAsUrl()
    {
        var url = "https://example.com/chart.png";
        var tm = new MediaContent(url, "image/png", MediaContentKind.Image);
        var messages = new List<ChatMessage>
        {
            Assistant("Chart below.", toolMedia: [tm]),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, $"![media]({url})");
        Assert.AreEqual(0, result.Media.Count);
    }

    [TestMethod]
    public void Export_ThinkingContent_DetailsBlock()
    {
        var messages = new List<ChatMessage>
        {
            Assistant("Final answer.", thinkingContent: "Let me reason through this..."),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "<summary>Thinking</summary>");
        StringAssert.Contains(result.Markdown, "Let me reason through this...");
    }

    [TestMethod]
    public void Export_Frontmatter_ContainsMetadata()
    {
        var messages = new List<ChatMessage>
        {
            User("Hello"),
            Assistant("World"),
        };

        var result = MarkdownExporter.Export(messages, title: "Test Chat");

        StringAssert.Contains(result.Markdown, "title: \"Test Chat\"");
        Assert.IsFalse(result.Markdown.Contains("provider:"));
        Assert.IsFalse(result.Markdown.Contains("model:"));
        StringAssert.Contains(result.Markdown, "message_count: 2");
        StringAssert.Contains(result.Markdown, "created:");
    }

    [TestMethod]
    public void Export_SystemMessage_Skipped()
    {
        var messages = new List<ChatMessage>
        {
            SystemMsg("You are a helpful assistant."),
            User("Hi"),
            Assistant("Hello!"),
        };

        var result = MarkdownExporter.Export(messages);

        Assert.IsFalse(result.Markdown.Contains("You are a helpful assistant."));
        StringAssert.Contains(result.Markdown, "message_count: 2");
    }

    [TestMethod]
    public void Export_MultipleImages_IncrementalNaming()
    {
        var img1 = new ChatAttachment("a.png", AttachmentType.Image, [0x01], "image/png");
        var img2 = new ChatAttachment("b.jpg", AttachmentType.Image, [0x02], "image/jpeg");
        var messages = new List<ChatMessage>
        {
            User("Two images", attachments: [img1, img2]),
        };

        var result = MarkdownExporter.Export(messages);

        Assert.IsTrue(result.Media.ContainsKey("media/img_001.png"));
        Assert.IsTrue(result.Media.ContainsKey("media/img_002.jpg"));
        StringAssert.Contains(result.Markdown, "![a.png](media/img_001.png)");
        StringAssert.Contains(result.Markdown, "![b.jpg](media/img_002.jpg)");
    }

    [TestMethod]
    public void Export_ToolOnlyTurn_NoEmptyTextSection()
    {
        var tc = new ToolCall
        {
            Id = "tc_1",
            FunctionName = "get_weather",
            Arguments = "{\"city\":\"Seoul\"}"
        };
        var messages = new List<ChatMessage>
        {
            User("What's the weather?"),
            Assistant("", toolCalls: [tc]),
            Tool("tc_1", "Sunny, 22°C"),
            Assistant("It's sunny and 22°C in Seoul."),
        };

        var result = MarkdownExporter.Export(messages);

        // After summary line and blank line, next non-blank should be tool <details>, not empty text.
        var lines = result.Markdown.Split('\n');
        var summaryIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("<summary><b>Assistant</b></summary>"))
            {
                summaryIdx = i;
                break;
            }
        }

        Assert.IsTrue(summaryIdx >= 0, "Should have Assistant summary line");

        var nextContentIdx = summaryIdx + 1;
        while (nextContentIdx < lines.Length && string.IsNullOrWhiteSpace(lines[nextContentIdx]))
            nextContentIdx++;

        Assert.AreEqual("<details>", lines[nextContentIdx].Trim(),
            "After empty-content assistant, should go straight to tool <details> block");
    }

    [TestMethod]
    public void Export_AssistantAttribution_IncludesProviderAndModel()
    {
        var messages = new List<ChatMessage>
        {
            User("Hi"),
            Assistant("Hello!", providerName: "OpenAI", providerModelId: "gpt-4o"),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "<summary><b>Assistant</b> · OpenAI · gpt-4o</summary>");
    }

    [TestMethod]
    public void Export_AssistantAttribution_MixedProviders()
    {
        var messages = new List<ChatMessage>
        {
            User("Hi"),
            Assistant("Hello from OpenAI!", providerName: "OpenAI", providerModelId: "gpt-4o"),
            User("Switch model"),
            Assistant("Hello from Claude!", providerName: "Anthropic", providerModelId: "claude-sonnet-4-6"),
        };

        var result = MarkdownExporter.Export(messages);

        StringAssert.Contains(result.Markdown, "<b>Assistant</b> · OpenAI · gpt-4o");
        StringAssert.Contains(result.Markdown, "<b>Assistant</b> · Anthropic · claude-sonnet-4-6");
    }

    [TestMethod]
    public void Export_AssistantAttribution_NoProvider_NoSuffix()
    {
        var messages = new List<ChatMessage>
        {
            User("Hi"),
            Assistant("Hello!"),
        };

        var result = MarkdownExporter.Export(messages);
        var lines = result.Markdown.Split('\n');
        var summary = lines.First(l => l.Contains("<summary><b>Assistant</b>")).TrimEnd();

        Assert.AreEqual("<summary><b>Assistant</b></summary>", summary);
    }

    [TestMethod]
    public void Export_DetailsOpenStructure_AllMessagesWrapped()
    {
        var messages = new List<ChatMessage>
        {
            User("Hello"),
            Assistant("World"),
        };

        var result = MarkdownExporter.Export(messages);

        // Count <details open> and </details> — should have 2 pairs (User + Assistant)
        var openCount = result.Markdown.Split("<details open>").Length - 1;
        var closeCount = result.Markdown.Split("</details>").Length - 1;

        Assert.AreEqual(2, openCount, "Should have 2 <details open> blocks");
        Assert.AreEqual(2, closeCount, "Should have 2 </details> closings");
    }

    [TestMethod]
    public void Export_ToolResultNotDuplicated()
    {
        var tc = new ToolCall
        {
            Id = "tc_1",
            FunctionName = "delegate_task",
            Arguments = "{\"prompt\":\"search\"}"
        };
        var messages = new List<ChatMessage>
        {
            User("Do something"),
            Assistant("", toolCalls: [tc]),
            Tool("tc_1", "task completed successfully"),
            Assistant("Done!"),
        };

        var result = MarkdownExporter.Export(messages);

        // The tool result should appear exactly once — inside the Assistant's <details> block,
        // not a second time as a standalone "Tool Result" block.
        var occurrences = result.Markdown.Split("task completed successfully").Length - 1;
        Assert.AreEqual(1, occurrences, "Tool result should appear exactly once, not duplicated");

        // Should NOT have an orphan "Tool Result" block
        Assert.IsFalse(result.Markdown.Contains("<summary>Tool Result</summary>"),
            "Matched tool results should not produce orphan blocks");
    }
}
