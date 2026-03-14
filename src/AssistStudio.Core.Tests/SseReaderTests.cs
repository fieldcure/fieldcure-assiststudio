using System.Text;
using FieldCure.AssistStudio.Providers;

namespace FieldCure.AssistStudio.Tests;

[TestClass]
public class SseReaderTests
{
    private static MemoryStream ToStream(string content) =>
        new(Encoding.UTF8.GetBytes(content));

    private static async Task<List<SseEvent>> CollectAsync(string content)
    {
        using var stream = ToStream(content);
        var events = new List<SseEvent>();
        await foreach (var e in SseReader.ReadEventsAsync(stream))
            events.Add(e);
        return events;
    }

    [TestMethod]
    public async Task BasicEventAndData_Parsed()
    {
        var events = await CollectAsync("event: content_block_delta\ndata: {\"text\":\"hi\"}\n\n");
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("content_block_delta", events[0].EventType);
        Assert.AreEqual("{\"text\":\"hi\"}", events[0].Data);
    }

    [TestMethod]
    public async Task NoEventField_DefaultsToMessage()
    {
        var events = await CollectAsync("data: hello\n\n");
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("message", events[0].EventType);
        Assert.AreEqual("hello", events[0].Data);
    }

    [TestMethod]
    public async Task TrailingData_WithoutBlankLine_Flushed()
    {
        var events = await CollectAsync("data: trailing");
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("trailing", events[0].Data);
    }

    [TestMethod]
    public async Task EmptyStream_ReturnsNoEvents()
    {
        var events = await CollectAsync("");
        Assert.AreEqual(0, events.Count);
    }

    [TestMethod]
    public async Task MultiLineData_JoinedWithNewline()
    {
        var events = await CollectAsync("data: line1\ndata: line2\n\n");
        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("line1\nline2", events[0].Data);
    }
}
