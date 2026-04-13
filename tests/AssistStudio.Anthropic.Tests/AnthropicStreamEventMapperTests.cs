using Anthropic.Models.Messages;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Anthropic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace FieldCure.AssistStudio.Anthropic.Tests;

[TestClass]
public class AnthropicStreamEventMapperTests
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
    public async Task TextOnly_MapsTextDeltas()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(inputTokens: 10),
            SdkEvents.TextBlockStart(0),
            SdkEvents.TextDeltaEvent(0, "Hello"),
            SdkEvents.TextDeltaEvent(0, " World"),
            SdkEvents.TextDeltaEvent(0, "!"),
            SdkEvents.BlockStop(0),
            SdkEvents.MessageDelta(outputTokens: 5),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        Assert.AreEqual(5, events.Count); // 3 TextDelta + Usage + StreamCompleted
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
        Assert.AreEqual("Hello", ((StreamEvent.TextDelta)events[0]).Text);
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[1]);
        Assert.AreEqual(" World", ((StreamEvent.TextDelta)events[1]).Text);
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[2]);
        Assert.AreEqual("!", ((StreamEvent.TextDelta)events[2]).Text);

        var usage = (StreamEvent.Usage)events[3];
        Assert.AreEqual(10, usage.TokenUsage.InputTokens);
        Assert.AreEqual(5, usage.TokenUsage.OutputTokens);

        var completed = (StreamEvent.StreamCompleted)events[4];
        Assert.IsFalse(completed.IsTruncated);
    }

    [TestMethod]
    public async Task ThinkingThenText_MapsCorrectly()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(),
            SdkEvents.ThinkingBlockStart(0),
            SdkEvents.ThinkingDeltaEvent(0, "Let me think..."),
            SdkEvents.ThinkingDeltaEvent(0, " Okay."),
            SdkEvents.BlockStop(0),
            SdkEvents.TextBlockStart(1),
            SdkEvents.TextDeltaEvent(1, "Answer"),
            SdkEvents.BlockStop(1),
            SdkEvents.MessageDelta(outputTokens: 10),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        // 2 ThinkingDelta + 1 TextDelta + Usage + StreamCompleted = 5
        Assert.AreEqual(5, events.Count);
        Assert.IsInstanceOfType<StreamEvent.ThinkingDelta>(events[0]);
        Assert.AreEqual("Let me think...", ((StreamEvent.ThinkingDelta)events[0]).Text);
        Assert.IsInstanceOfType<StreamEvent.ThinkingDelta>(events[1]);
        Assert.AreEqual(" Okay.", ((StreamEvent.ThinkingDelta)events[1]).Text);
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[2]);
        Assert.AreEqual("Answer", ((StreamEvent.TextDelta)events[2]).Text);
    }

    [TestMethod]
    public async Task OrphanDelta_Dropped()
    {
        // Delta event with index 99 that has no matching BlockStart
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(),
            SdkEvents.TextDeltaEvent(99, "orphan"),
            SdkEvents.MessageDelta(outputTokens: 1),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        // Only Usage + StreamCompleted; orphan delta dropped
        Assert.AreEqual(2, events.Count);
        Assert.IsInstanceOfType<StreamEvent.Usage>(events[0]);
        Assert.IsInstanceOfType<StreamEvent.StreamCompleted>(events[1]);
    }

    [TestMethod]
    public async Task MaxTokens_IsTruncatedTrue()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(),
            SdkEvents.TextBlockStart(0),
            SdkEvents.TextDeltaEvent(0, "partial"),
            SdkEvents.BlockStop(0),
            SdkEvents.MessageDelta(outputTokens: 4096, stopReason: StopReason.MaxTokens),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        var completed = events.OfType<StreamEvent.StreamCompleted>().Single();
        Assert.IsTrue(completed.IsTruncated);
    }

    [TestMethod]
    public async Task UnknownBlock_RedactedThinking_DeltasDropped()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(),
            SdkEvents.RedactedThinkingBlockStart(0),
            // These deltas target a block registered as Unknown → should be dropped
            SdkEvents.TextDeltaEvent(0, "should be dropped"),
            SdkEvents.BlockStop(0),
            SdkEvents.TextBlockStart(1),
            SdkEvents.TextDeltaEvent(1, "visible"),
            SdkEvents.BlockStop(1),
            SdkEvents.MessageDelta(outputTokens: 5),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        // Only "visible" text + Usage + StreamCompleted
        Assert.AreEqual(3, events.Count);
        var textDelta = (StreamEvent.TextDelta)events[0];
        Assert.AreEqual("visible", textDelta.Text);
    }

    [TestMethod]
    public async Task CitationsAndSignature_SilentlyDropped()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(),
            SdkEvents.TextBlockStart(0),
            SdkEvents.TextDeltaEvent(0, "text"),
            SdkEvents.CitationsDeltaEvent(0),
            SdkEvents.SignatureDeltaEvent(0),
            SdkEvents.BlockStop(0),
            SdkEvents.MessageDelta(outputTokens: 5),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        // TextDelta + Usage + StreamCompleted = 3 (citations & signature dropped)
        Assert.AreEqual(3, events.Count);
        Assert.IsInstanceOfType<StreamEvent.TextDelta>(events[0]);
    }

    [TestMethod]
    public async Task ClampToInt_BoundaryValues()
    {
        var source = SdkEvents.ToAsyncEnumerable(
            SdkEvents.MessageStart(inputTokens: long.MaxValue),
            SdkEvents.TextBlockStart(0),
            SdkEvents.TextDeltaEvent(0, "x"),
            SdkEvents.BlockStop(0),
            SdkEvents.MessageDelta(outputTokens: -1),
            SdkEvents.MessageStop()
        );

        var events = await CollectAsync(source);

        var usage = events.OfType<StreamEvent.Usage>().Single();
        Assert.AreEqual(int.MaxValue, usage.TokenUsage.InputTokens);
        Assert.AreEqual(0, usage.TokenUsage.OutputTokens); // negative clamped to 0
    }
}
