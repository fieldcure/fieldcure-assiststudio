using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Helpers;
using System.Text.Json;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests tool-call execution behavior, including argument handling, confirmation flow, and failures.
/// </summary>
[TestClass]
public class ToolCallExecutorTests
{
    private sealed class EchoTool : IAssistTool
    {
        public string Name => "echo";
        public string Description => "Echoes the input";
        public string ParameterSchema => """{"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}""";
        public bool RequiresConfirmation => false;

        public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
        {
            var text = parameters.GetProperty("text").GetString()!;
            return Task.FromResult(text);
        }
    }

    private sealed class ConfirmableTool : IAssistTool
    {
        public string Name => "dangerous";
        public string Description => "A tool that requires confirmation";
        public string ParameterSchema => """{"type":"object","properties":{}}""";
        public bool RequiresConfirmation => true;

        public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
            => Task.FromResult("executed");
    }

    [TestMethod]
    public async Task ExecuteAsync_CallsToolAndReturnsResult()
    {
        var executor = new ToolCallExecutor([new EchoTool()]);
        var call = new ToolCall { Id = "1", FunctionName = "echo", Arguments = """{"text":"hello"}""" };

        var result = await executor.ExecuteAsync(call);

        Assert.AreEqual("hello", result.Text);
    }

    [TestMethod]
    public async Task ExecuteAsync_ThrowsForUnknownTool()
    {
        var executor = new ToolCallExecutor([new EchoTool()]);
        var call = new ToolCall { Id = "1", FunctionName = "unknown", Arguments = "{}" };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(call));
    }

    [TestMethod]
    public async Task ExecuteAsync_SkipsWhenConfirmationDenied()
    {
        var executor = new ToolCallExecutor([new ConfirmableTool()])
        {
            ConfirmationHandler = (_, _) => Task.FromResult<(bool, string?)>((false, null))
        };
        var call = new ToolCall { Id = "1", FunctionName = "dangerous", Arguments = "{}" };

        var result = await executor.ExecuteAsync(call);

        Assert.IsTrue(result.Text.Contains("rejected"));
        Assert.IsTrue(result.Text.Contains("Tool call rejected by user."));
    }

    [TestMethod]
    public async Task ExecuteAsync_ProceedsWhenConfirmationApproved()
    {
        var executor = new ToolCallExecutor([new ConfirmableTool()])
        {
            ConfirmationHandler = (_, _) => Task.FromResult<(bool, string?)>((true, null))
        };
        var call = new ToolCall { Id = "1", FunctionName = "dangerous", Arguments = "{}" };

        var result = await executor.ExecuteAsync(call);

        Assert.AreEqual("executed", result.Text);
    }

    [TestMethod]
    public async Task ExecuteAsync_NoConfirmationHandler_ExecutesDirectly()
    {
        var executor = new ToolCallExecutor([new ConfirmableTool()]);
        var call = new ToolCall { Id = "1", FunctionName = "dangerous", Arguments = "{}" };

        var result = await executor.ExecuteAsync(call);

        Assert.AreEqual("executed", result.Text);
    }
}
