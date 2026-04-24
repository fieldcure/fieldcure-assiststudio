using FieldCure.Ai.Providers.Models;
using System.Net;
using System.Text.Json;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Verifies that <see cref="ClaudeProvider"/> correctly activates Anthropic prompt caching
/// (top-level <c>cache_control</c> marker) and parses the resulting usage fields.
/// </summary>
[TestClass]
public class ClaudePromptCachingTests
{
    #region Helpers

    private static AiRequest MakeRequest() => new()
    {
        Messages = [new ChatMessage(ChatRole.User, "Hello")],
        Temperature = 0.7,
        MaxTokens = 4096,
    };

    private static HttpClient MockClient(string body) =>
        new(new MockHandler(body)) { BaseAddress = new Uri("https://test.local/") };

    private sealed class MockHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }

    #endregion

    [TestMethod]
    public async Task BuildRequestBody_IncludesTopLevelCacheControl()
    {
        using var provider = new ClaudeProvider(MockClient("{}"), "test-key", "claude-sonnet-4-6");
        try { await provider.CompleteAsync(MakeRequest()); } catch { /* response parsing errors ignored */ }

        Assert.IsNotNull(provider.LastRequestBody);
        using var doc = JsonDocument.Parse(provider.LastRequestBody);
        var root = doc.RootElement;

        Assert.IsTrue(root.TryGetProperty("cache_control", out var cc),
            "body should include top-level cache_control for automatic prompt caching");
        Assert.AreEqual("ephemeral", cc.GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task ParseUsage_WithCacheFields_PopulatesTokenUsage()
    {
        const string response = """
            {
              "content": [{"type":"text","text":"hi"}],
              "stop_reason": "end_turn",
              "usage": {
                "input_tokens": 120,
                "output_tokens": 45,
                "cache_creation_input_tokens": 9800,
                "cache_read_input_tokens": 1500
              }
            }
            """;
        using var provider = new ClaudeProvider(MockClient(response), "test-key", "claude-sonnet-4-6");

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(120, result.Usage.InputTokens);
        Assert.AreEqual(45, result.Usage.OutputTokens);
        Assert.AreEqual(9800L, result.Usage.CacheCreationInputTokens);
        Assert.AreEqual(1500L, result.Usage.CacheReadInputTokens);
    }

    [TestMethod]
    public async Task ParseUsage_WithoutCacheFields_LeavesCacheTokensNull()
    {
        const string response = """
            {
              "content": [{"type":"text","text":"hi"}],
              "stop_reason": "end_turn",
              "usage": { "input_tokens": 50, "output_tokens": 20 }
            }
            """;
        using var provider = new ClaudeProvider(MockClient(response), "test-key", "claude-sonnet-4-6");

        var result = await provider.CompleteAsync(MakeRequest());

        Assert.IsNotNull(result.Usage);
        Assert.AreEqual(50, result.Usage.InputTokens);
        Assert.IsNull(result.Usage.CacheCreationInputTokens);
        Assert.IsNull(result.Usage.CacheReadInputTokens);
    }
}
