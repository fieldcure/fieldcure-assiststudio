using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using System.Net;
using System.Text.Json;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Verifies that each provider's BuildRequestBody correctly includes thinking parameters
/// in the JSON request body based on <see cref="AiRequest.ThinkingEnabled"/> and
/// <see cref="AiRequest.ThinkingBudget"/> settings.
/// </summary>
[TestClass]
public class BuildRequestBodyThinkingTests
{
    #region Helpers

    private static AiRequest MakeRequest(bool thinkingEnabled = false, int? thinkingBudget = null) => new()
    {
        Messages = [new ChatMessage(ChatRole.User, "Hello")],
        Temperature = 0.7,
        MaxTokens = 4096,
        ThinkingEnabled = thinkingEnabled,
        ThinkingBudget = thinkingBudget
    };

    /// <summary>
    /// Triggers a provider request and captures <see cref="IAiProvider.LastRequestBody"/>.
    /// Uses a mock handler that returns a minimal valid response for each provider format.
    /// </summary>
    private static async Task<JsonDocument> CaptureRequestBodyAsync(IAiProvider provider, AiRequest request)
    {
        try { await provider.CompleteAsync(request); } catch { /* ignore response parsing errors */ }
        Assert.IsNotNull(provider.LastRequestBody, "LastRequestBody should be set after CompleteAsync");
        return JsonDocument.Parse(provider.LastRequestBody);
    }

    private static HttpClient MockClient(string body = "{}", string contentType = "application/json") =>
        new(new MockHandler(body, contentType)) { BaseAddress = new Uri("https://test.local/") };

    private sealed class MockHandler(string body, string contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, contentType)
            });
    }

    #endregion

    #region Claude Provider

    [TestMethod]
    public async Task Claude_ThinkingEnabled_IncludesThinkingBlock()
    {
        using var provider = new ClaudeProvider(MockClient(), "test-key", "claude-sonnet-4-20250514");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 8192);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var root = doc.RootElement;

        // Should have thinking block with type and budget_tokens
        Assert.IsTrue(root.TryGetProperty("thinking", out var thinking));
        Assert.AreEqual("enabled", thinking.GetProperty("type").GetString());
        Assert.AreEqual(8192, thinking.GetProperty("budget_tokens").GetInt32());
    }

    [TestMethod]
    public async Task Claude_ThinkingEnabled_MinBudget1024()
    {
        using var provider = new ClaudeProvider(MockClient(), "test-key", "claude-sonnet-4-20250514");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 500);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var budget = doc.RootElement.GetProperty("thinking").GetProperty("budget_tokens").GetInt32();

        // Budget must be at least 1024 per Claude API spec
        Assert.IsTrue(budget >= 1024, $"budget_tokens should be >= 1024, was {budget}");
    }

    [TestMethod]
    public async Task Claude_ThinkingDisabled_NoThinkingBlock()
    {
        using var provider = new ClaudeProvider(MockClient(), "test-key", "claude-sonnet-4-20250514");
        var request = MakeRequest(thinkingEnabled: false);

        using var doc = await CaptureRequestBodyAsync(provider, request);

        Assert.IsFalse(doc.RootElement.TryGetProperty("thinking", out _),
            "thinking block should not be present when disabled");
    }

    [TestMethod]
    public async Task Claude_ThinkingEnabled_DefaultBudget16384()
    {
        using var provider = new ClaudeProvider(MockClient(), "test-key", "claude-sonnet-4-20250514");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: null);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var budget = doc.RootElement.GetProperty("thinking").GetProperty("budget_tokens").GetInt32();

        Assert.AreEqual(16384, budget);
    }

    #endregion

    #region OpenAI Provider

    [TestMethod]
    public async Task OpenAI_OSeriesModel_UsesMaxCompletionTokens()
    {
        using var provider = new OpenAiProvider(MockClient(), "test-key", "o3-mini");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 8192);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var root = doc.RootElement;

        // O-series uses max_completion_tokens, not max_tokens
        Assert.IsTrue(root.TryGetProperty("max_completion_tokens", out _));
        Assert.IsFalse(root.TryGetProperty("max_tokens", out _));
    }

    [TestMethod]
    public async Task OpenAI_OSeriesModel_HasReasoningEffort()
    {
        using var provider = new OpenAiProvider(MockClient(), "test-key", "o3-mini");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 8192);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("reasoning_effort", out var effort));
        var effortStr = effort.GetString();
        Assert.IsTrue(effortStr is "low" or "medium" or "high",
            $"reasoning_effort should be low/medium/high, was {effortStr}");
    }

    [TestMethod]
    public async Task OpenAI_OSeriesModel_NoTemperature()
    {
        using var provider = new OpenAiProvider(MockClient(), "test-key", "o3-mini");
        var request = MakeRequest(thinkingEnabled: true);

        using var doc = await CaptureRequestBodyAsync(provider, request);

        Assert.IsFalse(doc.RootElement.TryGetProperty("temperature", out _),
            "O-series models should not include temperature");
    }

    [TestMethod]
    public async Task OpenAI_StandardModel_UsesMaxTokens()
    {
        using var provider = new OpenAiProvider(MockClient(), "test-key", "gpt-4o");
        var request = MakeRequest(thinkingEnabled: false);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("max_tokens", out _));
        Assert.IsFalse(root.TryGetProperty("max_completion_tokens", out _));
        Assert.IsTrue(root.TryGetProperty("temperature", out _));
    }

    #endregion

    #region Gemini Provider

    [TestMethod]
    public async Task Gemini_25_ThinkingEnabled_UsesThinkingBudget()
    {
        var response = """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";
        using var provider = new GeminiProvider(MockClient(response), "test-key", "gemini-2.5-flash-preview-05-20");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 8192);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var genConfig = doc.RootElement.GetProperty("generationConfig");

        Assert.IsTrue(genConfig.TryGetProperty("thinkingConfig", out var tc));
        Assert.AreEqual(8192, tc.GetProperty("thinkingBudget").GetInt32());
    }

    [TestMethod]
    public async Task Gemini_25_ThinkingDisabled_BudgetZero()
    {
        var response = """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";
        using var provider = new GeminiProvider(MockClient(response), "test-key", "gemini-2.5-flash-preview-05-20");
        var request = MakeRequest(thinkingEnabled: false);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var genConfig = doc.RootElement.GetProperty("generationConfig");

        // Gemini 2.5 should explicitly disable thinking with budget = 0
        Assert.IsTrue(genConfig.TryGetProperty("thinkingConfig", out var tc));
        Assert.AreEqual(0, tc.GetProperty("thinkingBudget").GetInt32());
    }

    [TestMethod]
    public async Task Gemini_3_ThinkingEnabled_UsesThinkingLevel()
    {
        var response = """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";
        using var provider = new GeminiProvider(MockClient(response), "test-key", "gemini-3-flash-preview");
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 8192);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var genConfig = doc.RootElement.GetProperty("generationConfig");

        Assert.IsTrue(genConfig.TryGetProperty("thinkingConfig", out var tc));
        Assert.IsTrue(tc.TryGetProperty("thinkingLevel", out var level));
        Assert.IsFalse(tc.TryGetProperty("thinkingBudget", out _),
            "Gemini 3 should use thinkingLevel, not thinkingBudget");

        var levelStr = level.GetString();
        Assert.IsTrue(levelStr is "minimal" or "low" or "medium" or "high",
            $"thinkingLevel should be lowercase, was {levelStr}");
    }

    [TestMethod]
    public async Task Gemini_3Pro_Required_AlwaysIncludesThinking()
    {
        var response = """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";
        using var provider = new GeminiProvider(MockClient(response), "test-key", "gemini-3.1-pro-preview");
        // Even with thinking disabled, Pro should still include thinkingConfig
        var request = MakeRequest(thinkingEnabled: false);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var genConfig = doc.RootElement.GetProperty("generationConfig");

        Assert.IsTrue(genConfig.TryGetProperty("thinkingConfig", out var tc),
            "Gemini 3 Pro should always include thinkingConfig");
        Assert.IsTrue(tc.TryGetProperty("thinkingLevel", out _));
    }

    [TestMethod]
    public async Task Gemini_3Pro_NoMinimalLevel()
    {
        var response = """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";
        using var provider = new GeminiProvider(MockClient(response), "test-key", "gemini-3.1-pro-preview");
        // Budget <= 1024 would normally map to "minimal", but Pro doesn't support it
        var request = MakeRequest(thinkingEnabled: true, thinkingBudget: 512);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var level = doc.RootElement
            .GetProperty("generationConfig")
            .GetProperty("thinkingConfig")
            .GetProperty("thinkingLevel")
            .GetString();

        Assert.AreNotEqual("minimal", level,
            "Gemini 3 Pro should not use 'minimal' thinkingLevel");
        Assert.AreEqual("low", level);
    }

    [TestMethod]
    public async Task Gemini_NonThinking_NoThinkingConfig()
    {
        var response = """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";
        using var provider = new GeminiProvider(MockClient(response), "test-key", "gemini-2.0-flash");
        var request = MakeRequest(thinkingEnabled: false);

        using var doc = await CaptureRequestBodyAsync(provider, request);
        var genConfig = doc.RootElement.GetProperty("generationConfig");

        Assert.IsFalse(genConfig.TryGetProperty("thinkingConfig", out _),
            "Non-thinking Gemini models should not have thinkingConfig");
    }

    #endregion

    #region ThinkingSupport Detection

    [TestMethod]
    [DataRow("claude-sonnet-4-20250514", ThinkingSupport.Optional)]
    [DataRow("claude-opus-4-20250514", ThinkingSupport.Optional)]
    [DataRow("claude-3.5-haiku-20241022", ThinkingSupport.NotSupported)]
    public void Claude_GetThinkingSupport(string modelId, ThinkingSupport expected) =>
        Assert.AreEqual(expected, ClaudeProvider.GetThinkingSupportFor(modelId));

    [TestMethod]
    [DataRow("o3-mini", ThinkingSupport.Optional)]
    [DataRow("o4-mini", ThinkingSupport.Optional)]
    [DataRow("gpt-4o", ThinkingSupport.NotSupported)]
    [DataRow("gpt-4-turbo", ThinkingSupport.NotSupported)]
    public void OpenAI_GetThinkingSupport(string modelId, ThinkingSupport expected) =>
        Assert.AreEqual(expected, OpenAiProvider.GetThinkingSupportFor(modelId));

    [TestMethod]
    [DataRow("gemini-2.5-flash-preview-05-20", ThinkingSupport.Optional)]
    [DataRow("gemini-3-flash-preview", ThinkingSupport.Optional)]
    [DataRow("gemini-3.1-pro-preview", ThinkingSupport.Required)]
    [DataRow("gemini-2.0-flash", ThinkingSupport.NotSupported)]
    public void Gemini_GetThinkingSupport(string modelId, ThinkingSupport expected) =>
        Assert.AreEqual(expected, GeminiProvider.GetThinkingSupportFor(modelId));

    [TestMethod]
    [DataRow("deepseek-r1:14b", ThinkingSupport.Optional)]
    [DataRow("qwq:32b", ThinkingSupport.Optional)]
    [DataRow("llama3.1", ThinkingSupport.NotSupported)]
    public void Ollama_GetThinkingSupport(string modelId, ThinkingSupport expected) =>
        Assert.AreEqual(expected, OllamaProvider.GetThinkingSupportFor(modelId));

    [TestMethod]
    public void ThinkingCapability_RoutesToProvider()
    {
        Assert.AreEqual(ThinkingSupport.Optional,
            ThinkingCapability.GetSupport("Claude", "claude-sonnet-4-20250514"));
        Assert.AreEqual(ThinkingSupport.Optional,
            ThinkingCapability.GetSupport("OpenAI", "o3-mini"));
        Assert.AreEqual(ThinkingSupport.Required,
            ThinkingCapability.GetSupport("Gemini", "gemini-3.1-pro-preview"));
        Assert.AreEqual(ThinkingSupport.NotSupported,
            ThinkingCapability.GetSupport("Unknown", "some-model"));
    }

    #endregion
}
