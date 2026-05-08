using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Helpers;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests synthesis of cancel <c>tool_result</c> messages for orphan tool calls
/// left dangling after a user STOP.
/// </summary>
[TestClass]
public class OrphanToolCancelInjectorTests
{
    /// <summary>Builds an assistant message with the given tool calls.</summary>
    private static ChatMessage Assistant(params (string Id, string Name)[] toolCalls)
        => new(ChatRole.Assistant, "")
        {
            ToolCalls = toolCalls
                .Select(t => new ToolCall { Id = t.Id, FunctionName = t.Name, Arguments = "{}" })
                .ToList(),
        };

    /// <summary>Builds a tool-result message for the given tool call id.</summary>
    private static ChatMessage Tool(string toolCallId, string content)
        => new(ChatRole.Tool, content) { ToolCallId = toolCallId };

    [TestMethod]
    public void NoToolCalls_IsNoOp()
    {
        IReadOnlyList<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "hi"),
            new ChatMessage(ChatRole.Assistant, "hello"),
            new ChatMessage(ChatRole.User, "again"),
        ];

        var output = OrphanToolCancelInjector.Inject(input);
        Assert.AreEqual(3, output.Count);
        for (var i = 0; i < input.Count; i++)
            Assert.AreSame(input[i], output[i]);
    }

    [TestMethod]
    public void AllMatched_PreservesOrderAndIdentities()
    {
        IReadOnlyList<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "do two things"),
            Assistant(("t1", "search"), ("t2", "fetch")),
            Tool("t1", "result A"),
            Tool("t2", "result B"),
            new ChatMessage(ChatRole.User, "thanks"),
        ];

        var output = OrphanToolCancelInjector.Inject(input);
        Assert.AreEqual(5, output.Count);
        Assert.AreSame(input[2], output[2]);
        Assert.AreSame(input[3], output[3]);
    }

    [TestMethod]
    public void TrailingOrphan_IsSynthesized()
    {
        IReadOnlyList<ChatMessage> input =
        [
            new ChatMessage(ChatRole.User, "do two things"),
            Assistant(("t1", "search"), ("t2", "fetch")),
            Tool("t1", "result A"),
            // t2 was STOPped before producing a result.
            new ChatMessage(ChatRole.User, "never mind, do this instead"),
        ];

        var output = OrphanToolCancelInjector.Inject(input);
        Assert.AreEqual(5, output.Count);
        Assert.AreEqual(ChatRole.Tool, output[2].Role);
        Assert.AreEqual("t1", output[2].ToolCallId);
        Assert.AreEqual(ChatRole.Tool, output[3].Role);
        Assert.AreEqual("t2", output[3].ToolCallId);
        Assert.AreEqual(OrphanToolCancelInjector.CanceledToolResultContent, output[3].Content);
    }

    [TestMethod]
    public void MiddleOrphan_IsInsertedInDeclarationOrder()
    {
        IReadOnlyList<ChatMessage> input =
        [
            Assistant(("t1", "a"), ("t2", "b"), ("t3", "c")),
            Tool("t1", "result 1"),
            // t2 orphan
            Tool("t3", "result 3"),
        ];

        var output = OrphanToolCancelInjector.Inject(input);
        Assert.AreEqual(4, output.Count);
        Assert.AreEqual("t1", output[1].ToolCallId);
        Assert.AreEqual("t2", output[2].ToolCallId);
        Assert.AreEqual(OrphanToolCancelInjector.CanceledToolResultContent, output[2].Content);
        Assert.AreEqual("t3", output[3].ToolCallId);
        Assert.AreEqual("result 3", output[3].Content);
    }

    /// <summary>
    /// Gemini-style: same function called twice, ids suffixed with index.
    /// Provider matches positionally after suffix-stripping; reordered output
    /// must keep the cancel synthesis in the second slot, not the first.
    /// </summary>
    [TestMethod]
    public void SameFunctionDoubled_OrphanInSecondSlot_PositionalOrderingPreserved()
    {
        IReadOnlyList<ChatMessage> input =
        [
            Assistant(("search_0", "search"), ("search_1", "search")),
            Tool("search_0", "result A"),
            // search_1 orphan
        ];

        var output = OrphanToolCancelInjector.Inject(input);
        Assert.AreEqual(3, output.Count);
        Assert.AreEqual("search_0", output[1].ToolCallId);
        Assert.AreEqual("result A", output[1].Content);
        Assert.AreEqual("search_1", output[2].ToolCallId);
        Assert.AreEqual(OrphanToolCancelInjector.CanceledToolResultContent, output[2].Content);
    }

    [TestMethod]
    public void OutOfOrderFollowers_AreReorderedToToolCallsOrder()
    {
        // Hypothetical malformed history where followers are out of declaration order.
        IReadOnlyList<ChatMessage> input =
        [
            Assistant(("t1", "a"), ("t2", "b")),
            Tool("t2", "result 2"),
            Tool("t1", "result 1"),
        ];

        var output = OrphanToolCancelInjector.Inject(input);
        Assert.AreEqual(3, output.Count);
        Assert.AreEqual("t1", output[1].ToolCallId);
        Assert.AreEqual("result 1", output[1].Content);
        Assert.AreEqual("t2", output[2].ToolCallId);
        Assert.AreEqual("result 2", output[2].Content);
    }
}
