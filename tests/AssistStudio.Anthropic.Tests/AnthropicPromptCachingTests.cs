using Anthropic.Models.Messages;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Anthropic.Tests;

/// <summary>
/// Verifies that <see cref="AnthropicStreamEventMapper"/> propagates Anthropic prompt-cache
/// usage fields (<c>cache_creation_input_tokens</c>, <c>cache_read_input_tokens</c>) from
/// SDK stream events into the public <see cref="StreamEvent.Usage"/> emission.
/// </summary>
[TestClass]
public class AnthropicPromptCachingTests
{
    private static async Task<List<StreamEvent>> CollectAsync(IAsyncEnumerable<RawMessageStreamEvent> source)
    {
        var mapper = new AnthropicStreamEventMapper();
        var result = new List<StreamEvent>();
        await foreach (var evt in mapper.MapAsync(source))
            result.Add(evt);
        return result;
    }

    [TestMethod]
    public async Task Mapper_PropagatesCacheTokens_FromMessageStart()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(
                inputTokens: 200,
                cacheCreationInputTokens: 9800,
                cacheReadInputTokens: 1500),
            SdkEvents.TextBlockStart(0),
            SdkEvents.TextDeltaEvent(0, "ok"),
            SdkEvents.BlockStop(0),
            SdkEvents.MessageDelta(outputTokens: 30),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        var usageEvent = events.OfType<StreamEvent.Usage>().Single();
        Assert.AreEqual(200, usageEvent.TokenUsage.InputTokens);
        Assert.AreEqual(30, usageEvent.TokenUsage.OutputTokens);
        Assert.AreEqual(9800L, usageEvent.TokenUsage.CacheCreationInputTokens);
        Assert.AreEqual(1500L, usageEvent.TokenUsage.CacheReadInputTokens);
    }

    [TestMethod]
    public async Task Mapper_LeavesCacheTokensNull_WhenNotReported()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(inputTokens: 50),
            SdkEvents.TextBlockStart(0),
            SdkEvents.TextDeltaEvent(0, "ok"),
            SdkEvents.BlockStop(0),
            SdkEvents.MessageDelta(outputTokens: 10),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        var usageEvent = events.OfType<StreamEvent.Usage>().Single();
        Assert.IsNull(usageEvent.TokenUsage.CacheCreationInputTokens);
        Assert.IsNull(usageEvent.TokenUsage.CacheReadInputTokens);
    }
}
