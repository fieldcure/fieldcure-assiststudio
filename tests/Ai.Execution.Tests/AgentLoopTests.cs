using FieldCure.Ai.Execution.Tests.Mocks;
using FieldCure.Ai.Providers.Models;
using MockProvider = FieldCure.Ai.Execution.Tests.Mocks.MockProvider;

namespace FieldCure.Ai.Execution.Tests;

/// <summary>
/// Tests the agent loop's completion, guard, error, and message flow behavior.
/// </summary>
[TestClass]
public sealed class AgentLoopTests
{
    readonly AgentLoop _loop = new();

    #region Completion Tests

    [TestMethod]
    public async Task RunAsync_NoTools_CompletesInOneRound()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Here is my answer.");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "You are helpful.",
            UserPrompt = "Hello",
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Completed, result.Status);
        Assert.AreEqual(1, result.RoundsExecuted);
        Assert.AreEqual("Here is my answer.", result.Summary);
        Assert.AreEqual(0, result.ToolCallCount);
        Assert.IsNull(result.ErrorMessage);
    }

    [TestMethod]
    public async Task RunAsync_WithToolCall_CompletesInTwoRounds()
    {
        var provider = new MockProvider();
        var tool = new MockTool("search", "found 3 results");

        // Round 1: LLM wants to call a tool
        provider.EnqueueToolCallResponse("Let me search...",
            new ToolCall
            {
                Id = "call_1",
                FunctionName = "search",
                Arguments = """{"query":"test"}""",
            });

        // Round 2: LLM responds with final answer
        provider.EnqueueResponse("Based on the search, here are the results.");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "You are helpful.",
            UserPrompt = "Search for test",
            Tools = [tool],
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Completed, result.Status);
        Assert.AreEqual(2, result.RoundsExecuted);
        Assert.AreEqual(1, result.ToolCallCount);
        Assert.AreEqual("Based on the search, here are the results.", result.Summary);
        Assert.AreEqual(1, tool.CallCount);
    }

    [TestMethod]
    public async Task RunAsync_MultipleToolCalls_AllExecuted()
    {
        var provider = new MockProvider();
        var tool1 = new MockTool("search", "results A");
        var tool2 = new MockTool("fetch", "data B");

        // Round 1: LLM calls two tools
        provider.EnqueueToolCallResponse("Searching and fetching...",
            new ToolCall { Id = "c1", FunctionName = "search", Arguments = """{"q":"a"}""" },
            new ToolCall { Id = "c2", FunctionName = "fetch", Arguments = """{"url":"b"}""" });

        // Round 2: Done
        provider.EnqueueResponse("Complete.");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
            Tools = [tool1, tool2],
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Completed, result.Status);
        Assert.AreEqual(2, result.ToolCallCount);
        Assert.AreEqual(1, tool1.CallCount);
        Assert.AreEqual(1, tool2.CallCount);
    }

    #endregion

    #region Guard Tests

    [TestMethod]
    public async Task RunAsync_MaxRoundsReached_ReturnsMaxRoundsStatus()
    {
        var provider = new MockProvider();
        var tool = new MockTool("search", "result");

        // Every round calls a tool → never completes naturally
        for (var i = 0; i < 3; i++)
        {
            provider.EnqueueToolCallResponse($"Round {i + 1}",
                new ToolCall { Id = $"c{i}", FunctionName = "search", Arguments = "{}" });
        }

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
            Tools = [tool],
            MaxRounds = 3,
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.MaxRoundsReached, result.Status);
        Assert.AreEqual(3, result.RoundsExecuted);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsTrue(result.ErrorMessage.Contains("3"));
    }

    [TestMethod]
    public async Task RunAsync_Cancellation_ThrowsOperationCancelled()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("This should not be reached.");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
        };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => _loop.RunAsync(context, cts.Token));
    }

    #endregion

    #region Error Handling Tests

    [TestMethod]
    public async Task RunAsync_ToolNotFound_ReturnsErrorInMessage()
    {
        var provider = new MockProvider();

        // LLM calls a tool that doesn't exist
        provider.EnqueueToolCallResponse("Calling missing tool",
            new ToolCall { Id = "c1", FunctionName = "nonexistent", Arguments = "{}" });

        // After error feedback, LLM completes
        provider.EnqueueResponse("Tool was not found, here is my fallback.");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
            Tools = [new MockTool("other_tool")],
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Completed, result.Status);
        Assert.AreEqual(1, result.ToolCallCount); // Attempted but failed

        // Verify error message was sent back to LLM
        var secondRequest = provider.ReceivedRequests[1];
        var toolMessage = secondRequest.Messages
            .FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.IsNotNull(toolMessage);
        Assert.IsTrue(toolMessage.Content!.Contains("not found"));
    }

    [TestMethod]
    public async Task RunAsync_ToolThrows_ErrorFedBackToLlm()
    {
        var provider = new MockProvider();
        var failTool = MockTool.Failing("broken_tool", "Something went wrong");

        // LLM calls the broken tool
        provider.EnqueueToolCallResponse("Calling broken tool",
            new ToolCall { Id = "c1", FunctionName = "broken_tool", Arguments = "{}" });

        // After error feedback, LLM completes
        provider.EnqueueResponse("I see the tool failed.");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
            Tools = [failTool],
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Completed, result.Status);

        // Verify error was fed back
        var secondRequest = provider.ReceivedRequests[1];
        var toolMessage = secondRequest.Messages
            .FirstOrDefault(m => m.Role == ChatRole.Tool);
        Assert.IsNotNull(toolMessage);
        Assert.IsTrue(toolMessage.Content!.Contains("Something went wrong"));
    }

    [TestMethod]
    public async Task RunAsync_ProviderThrows_ReturnsFailed()
    {
        var provider = new MockProvider();
        // No responses enqueued → will throw

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Failed, result.Status);
        Assert.IsNotNull(result.ErrorMessage);
    }

    #endregion

    #region Message Flow Tests

    [TestMethod]
    public async Task RunAsync_SystemPrompt_PassedToProvider()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("OK");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "Custom system prompt here",
            UserPrompt = "Hello",
        };

        await _loop.RunAsync(context);

        Assert.AreEqual(1, provider.ReceivedRequests.Count);
        Assert.AreEqual("Custom system prompt here", provider.ReceivedRequests[0].SystemPrompt);
    }

    [TestMethod]
    public async Task RunAsync_ToolResults_AccumulateInMessages()
    {
        var provider = new MockProvider();
        var tool = new MockTool("search", "found it");

        // Round 1: tool call
        provider.EnqueueToolCallResponse("Searching",
            new ToolCall { Id = "c1", FunctionName = "search", Arguments = "{}" });
        // Round 2: another tool call
        provider.EnqueueToolCallResponse("More searching",
            new ToolCall { Id = "c2", FunctionName = "search", Arguments = "{}" });
        // Round 3: done
        provider.EnqueueResponse("All done.");

        var context = new AgentLoopContext
        {
            Provider = provider,
            SystemPrompt = "sys",
            UserPrompt = "go",
            Tools = [tool],
        };

        var result = await _loop.RunAsync(context);

        Assert.AreEqual(AgentLoopStatus.Completed, result.Status);
        Assert.AreEqual(3, result.RoundsExecuted);

        // Third request should have: user, assistant+tools, tool_result, assistant+tools, tool_result
        var thirdRequest = provider.ReceivedRequests[2];
        var toolMessages = thirdRequest.Messages
            .Where(m => m.Role == ChatRole.Tool).ToList();
        Assert.AreEqual(2, toolMessages.Count);
    }

    #endregion
}
