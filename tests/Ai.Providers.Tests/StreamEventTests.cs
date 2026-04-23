using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers.Tests;

/// <summary>
/// Tests streamed provider event models and their mapping behavior.
/// </summary>
[TestClass]
public class StreamEventTests
{
    [TestMethod]
    public void TextDelta_RecordEquality()
    {
        var a = new StreamEvent.TextDelta("hello");
        var b = new StreamEvent.TextDelta("hello");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void TextDelta_DifferentValues_NotEqual()
    {
        var a = new StreamEvent.TextDelta("hello");
        var b = new StreamEvent.TextDelta("world");
        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void ThinkingDelta_RecordEquality()
    {
        var a = new StreamEvent.ThinkingDelta("reasoning");
        var b = new StreamEvent.ThinkingDelta("reasoning");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ToolCallStart_RecordEquality()
    {
        var a = new StreamEvent.ToolCallStart("tc1", "search");
        var b = new StreamEvent.ToolCallStart("tc1", "search");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ToolCallDelta_RecordEquality()
    {
        var a = new StreamEvent.ToolCallDelta("tc1", "{\"q\":");
        var b = new StreamEvent.ToolCallDelta("tc1", "{\"q\":");
        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void Usage_WrapsTokenUsage()
    {
        var tokenUsage = new TokenUsage(10, 20);
        var evt = new StreamEvent.Usage(tokenUsage);
        Assert.AreEqual(30, evt.TokenUsage.TotalTokens);
        Assert.AreEqual(10, evt.TokenUsage.InputTokens);
        Assert.AreEqual(20, evt.TokenUsage.OutputTokens);
    }

    [TestMethod]
    public void StreamCompleted_CarriesTruncation()
    {
        var truncated = new StreamEvent.StreamCompleted(true);
        var notTruncated = new StreamEvent.StreamCompleted(false);
        Assert.IsTrue(truncated.IsTruncated);
        Assert.IsFalse(notTruncated.IsTruncated);
    }

    [TestMethod]
    public void PatternMatch_AllSubtypes()
    {
        StreamEvent[] events =
        [
            new StreamEvent.TextDelta("hi"),
            new StreamEvent.ThinkingDelta("hmm"),
            new StreamEvent.ToolCallStart("tc1", "fn"),
            new StreamEvent.ToolCallDelta("tc1", "{}"),
            new StreamEvent.Usage(new TokenUsage(1, 2)),
            new StreamEvent.StreamCompleted(false),
        ];

        var matched = 0;
        foreach (var evt in events)
        {
            switch (evt)
            {
                case StreamEvent.TextDelta: matched++; break;
                case StreamEvent.ThinkingDelta: matched++; break;
                case StreamEvent.ToolCallStart: matched++; break;
                case StreamEvent.ToolCallDelta: matched++; break;
                case StreamEvent.Usage: matched++; break;
                case StreamEvent.StreamCompleted: matched++; break;
            }
        }

        Assert.AreEqual(events.Length, matched);
    }

    [TestMethod]
    public void AllSubtypes_AreStreamEvent()
    {
        StreamEvent evt = new StreamEvent.TextDelta("x");
        Assert.IsInstanceOfType<StreamEvent>(evt);

        evt = new StreamEvent.ThinkingDelta("x");
        Assert.IsInstanceOfType<StreamEvent>(evt);

        evt = new StreamEvent.ToolCallStart("id", "fn");
        Assert.IsInstanceOfType<StreamEvent>(evt);

        evt = new StreamEvent.ToolCallDelta("id", "{}");
        Assert.IsInstanceOfType<StreamEvent>(evt);

        evt = new StreamEvent.Usage(new TokenUsage(0, 0));
        Assert.IsInstanceOfType<StreamEvent>(evt);

        evt = new StreamEvent.StreamCompleted(false);
        Assert.IsInstanceOfType<StreamEvent>(evt);
    }
}
