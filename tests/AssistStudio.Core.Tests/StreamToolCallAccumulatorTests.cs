using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Helpers;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests streamed tool-call accumulation across partial chunks and completion boundaries.
/// </summary>
[TestClass]
public class StreamToolCallAccumulatorTests
{
    [TestMethod]
    public void SingleToolCall_AccumulatesCorrectly()
    {
        var acc = new StreamToolCallAccumulator();
        acc.HandleStart(new StreamEvent.ToolCallStart("tc1", "search"));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("tc1", "{\"q\":"));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("tc1", " \"hello\"}"));

        Assert.IsTrue(acc.HasToolCalls);
        var calls = acc.Drain();

        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual("tc1", calls[0].Id);
        Assert.AreEqual("search", calls[0].FunctionName);
        Assert.AreEqual("{\"q\": \"hello\"}", calls[0].Arguments);
    }

    [TestMethod]
    public void ParallelToolCalls_InterleavedDeltas()
    {
        var acc = new StreamToolCallAccumulator();
        acc.HandleStart(new StreamEvent.ToolCallStart("a", "fn_a"));
        acc.HandleStart(new StreamEvent.ToolCallStart("b", "fn_b"));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("a", "{\"x\""));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("b", "{\"y\""));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("a", ":1}"));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("b", ":2}"));

        var calls = acc.Drain();
        Assert.AreEqual(2, calls.Count);

        var callA = calls.Single(c => c.Id == "a");
        var callB = calls.Single(c => c.Id == "b");
        Assert.AreEqual("{\"x\":1}", callA.Arguments);
        Assert.AreEqual("{\"y\":2}", callB.Arguments);
    }

    [TestMethod]
    public void EmptyArguments_ProducesEmptyString()
    {
        var acc = new StreamToolCallAccumulator();
        acc.HandleStart(new StreamEvent.ToolCallStart("tc1", "noop"));

        var calls = acc.Drain();
        Assert.AreEqual("", calls[0].Arguments);
    }

    [TestMethod]
    public void Drain_ClearsState()
    {
        var acc = new StreamToolCallAccumulator();
        acc.HandleStart(new StreamEvent.ToolCallStart("tc1", "fn"));
        acc.HandleDelta(new StreamEvent.ToolCallDelta("tc1", "{}"));

        var first = acc.Drain();
        Assert.AreEqual(1, first.Count);
        Assert.IsFalse(acc.HasToolCalls);

        var second = acc.Drain();
        Assert.AreEqual(0, second.Count);
    }

    [TestMethod]
    public void HasToolCalls_FalseWhenEmpty()
    {
        var acc = new StreamToolCallAccumulator();
        Assert.IsFalse(acc.HasToolCalls);
    }

    [TestMethod]
    public void Delta_ForUnknownId_IsIgnored()
    {
        var acc = new StreamToolCallAccumulator();
        acc.HandleDelta(new StreamEvent.ToolCallDelta("unknown", "data"));
        Assert.IsFalse(acc.HasToolCalls);
    }
}
