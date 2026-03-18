using System.Net;
using System.Text;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;

namespace FieldCure.AssistStudio.Tests;

/// <summary>
/// Integration tests that verify each provider's <see cref="IAiProvider.StreamAsync"/> method
/// yields the correct sequence of <see cref="StreamEvent"/> subtypes from canned API responses.
/// </summary>
[TestClass]
public class StreamAsyncTests
{
    #region Helpers

    /// <summary>
    /// Creates an <see cref="HttpClient"/> backed by a handler that returns the given content
    /// with the specified content type.
    /// </summary>
    private static HttpClient CreateMockClient(string responseContent, string contentType = "text/event-stream")
    {
        var handler = new MockHttpMessageHandler(responseContent, contentType);
        return new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
    }

    /// <summary>
    /// Collects all <see cref="StreamEvent"/> instances from a provider's streaming response.
    /// </summary>
    private static async Task<List<StreamEvent>> CollectEventsAsync(IAiProvider provider, AiRequest request)
    {
        var events = new List<StreamEvent>();
        await foreach (var evt in provider.StreamAsync(request))
        {
            events.Add(evt);
        }
        return events;
    }

    private static AiRequest SimpleRequest() => new()
    {
        Messages = [new ChatMessage(ChatRole.User, "Hello")]
    };

    private sealed class MockHttpMessageHandler(string responseContent, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseContent, Encoding.UTF8, contentType)
            };
            return Task.FromResult(response);
        }
    }

    #endregion

    #region Claude Provider

    [TestMethod]
    public async Task Claude_StreamAsync_YieldsTextDelta_Usage_StreamCompleted()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: message_start");
        sse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":25}}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Hello"}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":" world"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_delta");
        sse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":10},"delta":{"stop_reason":"end_turn"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_stop");
        sse.AppendLine("""data: {"type":"message_stop"}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new ClaudeProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        // Expect: TextDelta("Hello"), TextDelta(" world"), Usage(...), StreamCompleted(false)
        Assert.AreEqual(4, events.Count);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
        Assert.AreEqual("Hello", ((StreamEvent.TextDelta)events[0]).Text);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[1]);
        Assert.AreEqual(" world", ((StreamEvent.TextDelta)events[1]).Text);

        var usage = (StreamEvent.Usage)events[2];
        Assert.AreEqual(25, usage.TokenUsage.InputTokens);
        Assert.AreEqual(10, usage.TokenUsage.OutputTokens);

        var completed = (StreamEvent.StreamCompleted)events[3];
        Assert.IsFalse(completed.IsTruncated);
    }

    [TestMethod]
    public async Task Claude_StreamAsync_Truncated_SetsIsTruncated()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: message_start");
        sse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":10}}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"partial"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_delta");
        sse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":4096},"delta":{"stop_reason":"max_tokens"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_stop");
        sse.AppendLine("""data: {"type":"message_stop"}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new ClaudeProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsTrue(completed.IsTruncated);
    }

    [TestMethod]
    public async Task Claude_StreamAsync_Thinking_YieldsThinkingDelta()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: message_start");
        sse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":30}}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Let me reason"}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":" about this."}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"The answer is 42."}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_delta");
        sse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":20},"delta":{"stop_reason":"end_turn"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_stop");
        sse.AppendLine("""data: {"type":"message_stop"}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new ClaudeProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var thinkingDeltas = events.OfType<StreamEvent.ThinkingDelta>().ToList();
        Assert.AreEqual(2, thinkingDeltas.Count);
        Assert.AreEqual("Let me reason", thinkingDeltas[0].Text);
        Assert.AreEqual(" about this.", thinkingDeltas[1].Text);

        var textDeltas = events.OfType<StreamEvent.TextDelta>().ToList();
        Assert.AreEqual(1, textDeltas.Count);
        Assert.AreEqual("The answer is 42.", textDeltas[0].Text);
    }

    [TestMethod]
    public async Task Claude_StreamAsync_ThinkingThenTextThenToolCall()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: message_start");
        sse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":40}}}""");
        sse.AppendLine();
        // Thinking block
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":""}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"I need to search."}}""");
        sse.AppendLine();
        // Text block
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":1,"content_block":{"type":"text","text":""}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Searching..."}}""");
        sse.AppendLine();
        // Tool use block
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":2,"content_block":{"type":"tool_use","id":"toolu_99","name":"web_search"}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":2,"delta":{"type":"input_json_delta","partial_json":"{\"q\":\"test\"}"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_delta");
        sse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":25},"delta":{"stop_reason":"tool_use"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_stop");
        sse.AppendLine("""data: {"type":"message_stop"}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new ClaudeProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        Assert.AreEqual(1, events.OfType<StreamEvent.ThinkingDelta>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.TextDelta>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.ToolCallStart>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.ToolCallDelta>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.Usage>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.StreamCompleted>().Count());
    }

    #endregion

    #region OpenAI Provider

    [TestMethod]
    public async Task OpenAi_StreamAsync_YieldsTextDelta_Usage_StreamCompleted()
    {
        var sse = new StringBuilder();
        sse.AppendLine("data: " + """{"choices":[{"delta":{"content":"Hi"},"finish_reason":null}]}""");
        sse.AppendLine();
        sse.AppendLine("data: " + """{"choices":[{"delta":{"content":" there"},"finish_reason":null}]}""");
        sse.AppendLine();
        sse.AppendLine("data: " + """{"choices":[{"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":15,"completion_tokens":8}}""");
        sse.AppendLine();
        sse.AppendLine("data: [DONE]");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new OpenAiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        // Expect: TextDelta("Hi"), TextDelta(" there"), Usage(...), StreamCompleted(false)
        Assert.AreEqual(4, events.Count);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
        Assert.AreEqual("Hi", ((StreamEvent.TextDelta)events[0]).Text);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[1]);
        Assert.AreEqual(" there", ((StreamEvent.TextDelta)events[1]).Text);

        var usage = (StreamEvent.Usage)events[2];
        Assert.AreEqual(15, usage.TokenUsage.InputTokens);
        Assert.AreEqual(8, usage.TokenUsage.OutputTokens);

        var completed = (StreamEvent.StreamCompleted)events[3];
        Assert.IsFalse(completed.IsTruncated);
    }

    [TestMethod]
    public async Task OpenAi_StreamAsync_Truncated_SetsIsTruncated()
    {
        var sse = new StringBuilder();
        sse.AppendLine("data: " + """{"choices":[{"delta":{"content":"cut off"},"finish_reason":"length"}],"usage":{"prompt_tokens":10,"completion_tokens":100}}""");
        sse.AppendLine();
        sse.AppendLine("data: [DONE]");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new OpenAiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsTrue(completed.IsTruncated);
    }

    [TestMethod]
    public async Task OpenAi_StreamAsync_NoUsage_SkipsUsageEvent()
    {
        var sse = new StringBuilder();
        sse.AppendLine("data: " + """{"choices":[{"delta":{"content":"ok"},"finish_reason":"stop"}]}""");
        sse.AppendLine();
        sse.AppendLine("data: [DONE]");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new OpenAiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        // No Usage event when usage is not included
        Assert.AreEqual(0, events.OfType<StreamEvent.Usage>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.TextDelta>().Count());
        Assert.AreEqual(1, events.OfType<StreamEvent.StreamCompleted>().Count());
    }

    #endregion

    #region Gemini Provider

    [TestMethod]
    public async Task Gemini_StreamAsync_YieldsTextDelta_Usage_StreamCompleted()
    {
        var sse = new StringBuilder();
        sse.AppendLine("data: " + """{"candidates":[{"content":{"parts":[{"text":"Bonjour"}]}}]}""");
        sse.AppendLine();
        sse.AppendLine("data: " + """{"candidates":[{"content":{"parts":[{"text":" monde"}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":12,"candidatesTokenCount":5}}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new GeminiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        // Expect: TextDelta("Bonjour"), TextDelta(" monde"), Usage(...), StreamCompleted(false)
        Assert.AreEqual(4, events.Count);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
        Assert.AreEqual("Bonjour", ((StreamEvent.TextDelta)events[0]).Text);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[1]);
        Assert.AreEqual(" monde", ((StreamEvent.TextDelta)events[1]).Text);

        var usage = (StreamEvent.Usage)events[2];
        Assert.AreEqual(12, usage.TokenUsage.InputTokens);
        Assert.AreEqual(5, usage.TokenUsage.OutputTokens);

        var completed = (StreamEvent.StreamCompleted)events[3];
        Assert.IsFalse(completed.IsTruncated);
    }

    [TestMethod]
    public async Task Gemini_StreamAsync_Truncated_SetsIsTruncated()
    {
        var sse = new StringBuilder();
        sse.AppendLine("data: " + """{"candidates":[{"content":{"parts":[{"text":"partial"}]},"finishReason":"MAX_TOKENS"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":50}}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new GeminiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsTrue(completed.IsTruncated);
    }

    #endregion

    #region Ollama Provider

    [TestMethod]
    public async Task Ollama_StreamAsync_YieldsTextDelta_Usage_StreamCompleted()
    {
        var ndjson = new StringBuilder();
        ndjson.AppendLine("""{"model":"llama3.1","message":{"role":"assistant","content":"Hey"},"done":false}""");
        ndjson.AppendLine("""{"model":"llama3.1","message":{"role":"assistant","content":" you"},"done":false}""");
        ndjson.AppendLine("""{"model":"llama3.1","done":true,"done_reason":"stop","prompt_eval_count":20,"eval_count":6}""");

        using var http = CreateMockClient(ndjson.ToString(), "application/x-ndjson");
        var provider = new OllamaProvider(http);
        var events = await CollectEventsAsync(provider, SimpleRequest());

        // Expect: TextDelta("Hey"), TextDelta(" you"), Usage(...), StreamCompleted(false)
        Assert.AreEqual(4, events.Count);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
        Assert.AreEqual("Hey", ((StreamEvent.TextDelta)events[0]).Text);

        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[1]);
        Assert.AreEqual(" you", ((StreamEvent.TextDelta)events[1]).Text);

        var usage = (StreamEvent.Usage)events[2];
        Assert.AreEqual(20, usage.TokenUsage.InputTokens);
        Assert.AreEqual(6, usage.TokenUsage.OutputTokens);

        var completed = (StreamEvent.StreamCompleted)events[3];
        Assert.IsFalse(completed.IsTruncated);
    }

    [TestMethod]
    public async Task Ollama_StreamAsync_Truncated_SetsIsTruncated()
    {
        var ndjson = new StringBuilder();
        ndjson.AppendLine("""{"model":"llama3.1","message":{"role":"assistant","content":"cut"},"done":false}""");
        ndjson.AppendLine("""{"model":"llama3.1","done":true,"done_reason":"length","prompt_eval_count":10,"eval_count":100}""");

        using var http = CreateMockClient(ndjson.ToString(), "application/x-ndjson");
        var provider = new OllamaProvider(http);
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsTrue(completed.IsTruncated);
    }

    #endregion

    #region MockProvider

    [TestMethod]
    public async Task Mock_StreamAsync_YieldsTextDeltas_And_StreamCompleted()
    {
        var provider = new MockProvider { EventDelayMs = 0 };
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var textDeltas = events.OfType<StreamEvent.TextDelta>().ToList();
        Assert.IsTrue(textDeltas.Count > 0, "MockProvider should yield at least one TextDelta");

        // All text deltas joined should form non-empty content
        var fullText = string.Concat(textDeltas.Select(d => d.Text));
        Assert.IsTrue(fullText.Contains("Markdown"), "MockProvider should stream its built-in Markdown response");

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsFalse(completed.IsTruncated);
    }

    [TestMethod]
    public async Task Mock_ScriptedEvents_YieldsExactSequence()
    {
        var provider = new MockProvider
        {
            EventDelayMs = 0,
            ScriptedEvents =
            [
                new StreamEvent.ThinkingDelta("Let me think..."),
                new StreamEvent.ThinkingDelta(" about this."),
                new StreamEvent.TextDelta("The answer is 42."),
                new StreamEvent.Usage(new TokenUsage(50, 15)),
                new StreamEvent.StreamCompleted(false),
            ]
        };

        var events = await CollectEventsAsync(provider, SimpleRequest());

        Assert.AreEqual(5, events.Count);
        Assert.IsInstanceOfType<StreamEvent.ThinkingDelta>(events[0]);
        Assert.AreEqual("Let me think...", ((StreamEvent.ThinkingDelta)events[0]).Text);
        Assert.IsInstanceOfType<StreamEvent.ThinkingDelta>(events[1]);
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[2]);
        Assert.AreEqual("The answer is 42.", ((StreamEvent.TextDelta)events[2]).Text);

        var usage = (StreamEvent.Usage)events[3];
        Assert.AreEqual(50, usage.TokenUsage.InputTokens);
        Assert.AreEqual(15, usage.TokenUsage.OutputTokens);

        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(events[4]);
    }

    [TestMethod]
    public async Task Mock_ScriptedEvents_ToolCallSequence()
    {
        var provider = new MockProvider
        {
            EventDelayMs = 0,
            ScriptedEvents =
            [
                new StreamEvent.TextDelta("I'll search for that."),
                new StreamEvent.ToolCallStart("tc_1", "web_search"),
                new StreamEvent.ToolCallDelta("tc_1", """{"query":"""),
                new StreamEvent.ToolCallDelta("tc_1", """ "weather today"}"""),
                new StreamEvent.StreamCompleted(false),
            ]
        };

        var events = await CollectEventsAsync(provider, SimpleRequest());

        Assert.AreEqual(5, events.Count);

        var toolStart = (StreamEvent.ToolCallStart)events[1];
        Assert.AreEqual("tc_1", toolStart.Id);
        Assert.AreEqual("web_search", toolStart.FunctionName);

        var deltas = events.OfType<StreamEvent.ToolCallDelta>().ToList();
        Assert.AreEqual(2, deltas.Count);
        Assert.IsTrue(deltas.All(d => d.Id == "tc_1"));

        var args = string.Concat(deltas.Select(d => d.ArgumentsChunk));
        Assert.AreEqual("""{"query": "weather today"}""", args);
    }

    [TestMethod]
    public async Task Mock_ScriptedEvents_AutoAppendsStreamCompleted()
    {
        var provider = new MockProvider
        {
            EventDelayMs = 0,
            ScriptedEvents =
            [
                new StreamEvent.TextDelta("no explicit completion"),
            ]
        };

        var events = await CollectEventsAsync(provider, SimpleRequest());

        Assert.AreEqual(2, events.Count);
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(events[1]);
    }

    [TestMethod]
    public async Task Mock_SimulatedUsage_EmitsUsageEvent()
    {
        var provider = new MockProvider
        {
            EventDelayMs = 0,
            SimulatedUsage = new TokenUsage(100, 200)
        };

        var events = await CollectEventsAsync(provider, SimpleRequest());

        var usage = events.OfType<StreamEvent.Usage>().Single();
        Assert.AreEqual(100, usage.TokenUsage.InputTokens);
        Assert.AreEqual(200, usage.TokenUsage.OutputTokens);
        Assert.AreEqual(usage.TokenUsage, provider.LastUsage);
    }

    [TestMethod]
    public async Task Mock_SimulateTruncated_SetsIsTruncated()
    {
        var provider = new MockProvider
        {
            EventDelayMs = 0,
            SimulateTruncated = true
        };

        var events = await CollectEventsAsync(provider, SimpleRequest());

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsTrue(completed.IsTruncated);
        Assert.IsTrue(provider.IsTruncated);
    }

    #endregion

    #region Streaming Tool Calls

    [TestMethod]
    public async Task Claude_StreamAsync_ToolCalls_YieldsToolCallStartAndDelta()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: message_start");
        sse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":30}}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"toolu_01","name":"web_search"}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{\"query\":"}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":" \"weather\"}"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_delta");
        sse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":15},"delta":{"stop_reason":"tool_use"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_stop");
        sse.AppendLine("""data: {"type":"message_stop"}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new ClaudeProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var toolStart = events.OfType<StreamEvent.ToolCallStart>().Single();
        Assert.AreEqual("toolu_01", toolStart.Id);
        Assert.AreEqual("web_search", toolStart.FunctionName);

        var deltas = events.OfType<StreamEvent.ToolCallDelta>().ToList();
        Assert.AreEqual(2, deltas.Count);
        Assert.IsTrue(deltas.All(d => d.Id == "toolu_01"));

        var args = string.Concat(deltas.Select(d => d.ArgumentsChunk));
        Assert.AreEqual("{\"query\": \"weather\"}", args);
    }

    [TestMethod]
    public async Task Claude_StreamAsync_MixedTextAndToolCalls()
    {
        var sse = new StringBuilder();
        sse.AppendLine("event: message_start");
        sse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":20}}}""");
        sse.AppendLine();
        // Text block
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Let me search."}}""");
        sse.AppendLine();
        // Tool use block
        sse.AppendLine("event: content_block_start");
        sse.AppendLine("""data: {"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"toolu_02","name":"calculator"}}""");
        sse.AppendLine();
        sse.AppendLine("event: content_block_delta");
        sse.AppendLine("""data: {"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"expr\":\"2+2\"}"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_delta");
        sse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":12},"delta":{"stop_reason":"tool_use"}}""");
        sse.AppendLine();
        sse.AppendLine("event: message_stop");
        sse.AppendLine("""data: {"type":"message_stop"}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new ClaudeProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        // Should have text, tool start, tool delta, usage, completed
        Assert.AreEqual(1, events.OfType<StreamEvent.TextDelta>().Count());
        Assert.AreEqual("Let me search.", events.OfType<StreamEvent.TextDelta>().Single().Text);

        var toolStart = events.OfType<StreamEvent.ToolCallStart>().Single();
        Assert.AreEqual("calculator", toolStart.FunctionName);

        Assert.AreEqual(1, events.OfType<StreamEvent.ToolCallDelta>().Count());
    }

    [TestMethod]
    public async Task OpenAi_StreamAsync_ToolCalls_YieldsToolCallStartAndDelta()
    {
        var sse = new StringBuilder();
        // First chunk: tool call start with id and function name
        sse.AppendLine("data: " + """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_abc","type":"function","function":{"name":"get_weather","arguments":""}}]},"finish_reason":null}]}""");
        sse.AppendLine();
        // Argument chunks
        sse.AppendLine("data: " + """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"city\":"}}]},"finish_reason":null}]}""");
        sse.AppendLine();
        sse.AppendLine("data: " + """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":" \"Seoul\"}"}}]},"finish_reason":"tool_calls"}]}""");
        sse.AppendLine();
        sse.AppendLine("data: [DONE]");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new OpenAiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var toolStart = events.OfType<StreamEvent.ToolCallStart>().Single();
        Assert.AreEqual("call_abc", toolStart.Id);
        Assert.AreEqual("get_weather", toolStart.FunctionName);

        var deltas = events.OfType<StreamEvent.ToolCallDelta>().ToList();
        Assert.AreEqual(2, deltas.Count);
        var args = string.Concat(deltas.Select(d => d.ArgumentsChunk));
        Assert.AreEqual("{\"city\": \"Seoul\"}", args);
    }

    [TestMethod]
    public async Task OpenAi_StreamAsync_ParallelToolCalls()
    {
        var sse = new StringBuilder();
        // Two tool calls interleaved by index
        sse.AppendLine("data: " + """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_1","type":"function","function":{"name":"fn_a","arguments":""}},{"index":1,"id":"call_2","type":"function","function":{"name":"fn_b","arguments":""}}]},"finish_reason":null}]}""");
        sse.AppendLine();
        sse.AppendLine("data: " + """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"x\":1}"}},{"index":1,"function":{"arguments":"{\"y\":2}"}}]},"finish_reason":null}]}""");
        sse.AppendLine();
        sse.AppendLine("data: " + """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");
        sse.AppendLine();
        sse.AppendLine("data: [DONE]");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new OpenAiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var starts = events.OfType<StreamEvent.ToolCallStart>().ToList();
        Assert.AreEqual(2, starts.Count);
        Assert.AreEqual("fn_a", starts[0].FunctionName);
        Assert.AreEqual("fn_b", starts[1].FunctionName);

        var deltasA = events.OfType<StreamEvent.ToolCallDelta>().Where(d => d.Id == "call_1").ToList();
        var deltasB = events.OfType<StreamEvent.ToolCallDelta>().Where(d => d.Id == "call_2").ToList();
        Assert.AreEqual("{\"x\":1}", string.Concat(deltasA.Select(d => d.ArgumentsChunk)));
        Assert.AreEqual("{\"y\":2}", string.Concat(deltasB.Select(d => d.ArgumentsChunk)));
    }

    [TestMethod]
    public async Task Gemini_StreamAsync_FunctionCall_YieldsToolCallStartAndDelta()
    {
        var sse = new StringBuilder();
        sse.AppendLine("data: " + """{"candidates":[{"content":{"parts":[{"functionCall":{"name":"search","args":{"query":"test"}}}]},"finishReason":"STOP"}],"usageMetadata":{"promptTokenCount":10,"candidatesTokenCount":5}}""");
        sse.AppendLine();

        using var http = CreateMockClient(sse.ToString());
        var provider = new GeminiProvider(http, "fake-key");
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var toolStart = events.OfType<StreamEvent.ToolCallStart>().Single();
        Assert.AreEqual("search", toolStart.Id); // Single call uses funcName as ID
        Assert.AreEqual("search", toolStart.FunctionName);

        var toolDelta = events.OfType<StreamEvent.ToolCallDelta>().Single();
        Assert.AreEqual("search", toolDelta.Id);
        Assert.AreEqual("""{"query":"test"}""", toolDelta.ArgumentsChunk);
    }

    [TestMethod]
    public async Task Ollama_StreamAsync_ToolCalls_YieldsToolCallStartAndDelta()
    {
        var ndjson = new StringBuilder();
        ndjson.AppendLine("""{"model":"llama3.1","message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"calc","arguments":{"expr":"1+1"}}}]},"done":false}""");
        ndjson.AppendLine("""{"model":"llama3.1","done":true,"done_reason":"stop","prompt_eval_count":10,"eval_count":5}""");

        using var http = CreateMockClient(ndjson.ToString(), "application/x-ndjson");
        var provider = new OllamaProvider(http);
        var events = await CollectEventsAsync(provider, SimpleRequest());

        var toolStart = events.OfType<StreamEvent.ToolCallStart>().Single();
        Assert.AreEqual("calc", toolStart.FunctionName);

        var toolDelta = events.OfType<StreamEvent.ToolCallDelta>().Single();
        Assert.AreEqual(toolStart.Id, toolDelta.Id);
        Assert.IsTrue(toolDelta.ArgumentsChunk.Contains("\"expr\""));
    }

    #endregion

    #region Cross-Provider Contract

    [TestMethod]
    public async Task AllProviders_StreamCompleted_IsAlwaysLastEvent()
    {
        // Claude
        var claudeSse = new StringBuilder();
        claudeSse.AppendLine("event: message_start");
        claudeSse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":5}}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: content_block_delta");
        claudeSse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"a"}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: message_delta");
        claudeSse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":1},"delta":{"stop_reason":"end_turn"}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: message_stop");
        claudeSse.AppendLine("""data: {"type":"message_stop"}""");
        claudeSse.AppendLine();

        using var claudeHttp = CreateMockClient(claudeSse.ToString());
        var claudeEvents = await CollectEventsAsync(new ClaudeProvider(claudeHttp, "k"), SimpleRequest());
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(claudeEvents[^1]);

        // OpenAI
        var openAiSse = new StringBuilder();
        openAiSse.AppendLine("data: " + """{"choices":[{"delta":{"content":"a"},"finish_reason":"stop"}]}""");
        openAiSse.AppendLine();
        openAiSse.AppendLine("data: [DONE]");
        openAiSse.AppendLine();

        using var openAiHttp = CreateMockClient(openAiSse.ToString());
        var openAiEvents = await CollectEventsAsync(new OpenAiProvider(openAiHttp, "k"), SimpleRequest());
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(openAiEvents[^1]);

        // Gemini
        var geminiSse = new StringBuilder();
        geminiSse.AppendLine("data: " + """{"candidates":[{"content":{"parts":[{"text":"a"}]},"finishReason":"STOP"}]}""");
        geminiSse.AppendLine();

        using var geminiHttp = CreateMockClient(geminiSse.ToString());
        var geminiEvents = await CollectEventsAsync(new GeminiProvider(geminiHttp, "k"), SimpleRequest());
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(geminiEvents[^1]);

        // Ollama
        var ollamaNdjson = """{"model":"m","message":{"role":"assistant","content":"a"},"done":false}""" + "\n"
                         + """{"model":"m","done":true,"done_reason":"stop","prompt_eval_count":1,"eval_count":1}""" + "\n";

        using var ollamaHttp = CreateMockClient(ollamaNdjson, "application/x-ndjson");
        var ollamaEvents = await CollectEventsAsync(new OllamaProvider(ollamaHttp), SimpleRequest());
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(ollamaEvents[^1]);

        // Mock
        var mockEvents = await CollectEventsAsync(new MockProvider { EventDelayMs = 0 }, SimpleRequest());
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(mockEvents[^1]);
    }

    [TestMethod]
    public async Task AllProviders_TextContent_MatchesJoinedTextDeltas()
    {
        // Verify that joining all TextDelta.Text produces the expected full text

        // Claude
        var claudeSse = new StringBuilder();
        claudeSse.AppendLine("event: message_start");
        claudeSse.AppendLine("""data: {"type":"message_start","message":{"usage":{"input_tokens":5}}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: content_block_delta");
        claudeSse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"AB"}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: content_block_delta");
        claudeSse.AppendLine("""data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"CD"}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: message_delta");
        claudeSse.AppendLine("""data: {"type":"message_delta","usage":{"output_tokens":2},"delta":{"stop_reason":"end_turn"}}""");
        claudeSse.AppendLine();
        claudeSse.AppendLine("event: message_stop");
        claudeSse.AppendLine("""data: {"type":"message_stop"}""");
        claudeSse.AppendLine();

        using var http = CreateMockClient(claudeSse.ToString());
        var events = await CollectEventsAsync(new ClaudeProvider(http, "k"), SimpleRequest());
        var fullText = string.Concat(events.OfType<StreamEvent.TextDelta>().Select(d => d.Text));
        Assert.AreEqual("ABCD", fullText);
    }

    #endregion
}
