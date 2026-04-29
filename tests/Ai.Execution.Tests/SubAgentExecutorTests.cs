using FieldCure.Ai.Execution.Models;
using FieldCure.Ai.Execution.Tests.Mocks;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using MockProvider = FieldCure.Ai.Execution.Tests.Mocks.MockProvider;

namespace FieldCure.Ai.Execution.Tests;

/// <summary>
/// Tests sub-agent execution behavior, including result handling and delegation flow.
/// </summary>
[TestClass]
public sealed class SubAgentExecutorTests
{
    #region Basic Execution

    [TestMethod]
    public async Task ExecuteAsync_SimpleTask_ReturnsCompletedReport()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("## 결론\nTask completed successfully.");

        var executor = CreateExecutor(provider);

        var request = new SubAgentRequest
        {
            Prompt = "Do something",
        };

        var result = await executor.ExecuteAsync(request);

        Assert.AreEqual(SubAgentStatus.Completed, result.Status);
        Assert.AreEqual("## 결론\nTask completed successfully.", result.Report);
        Assert.IsNull(result.UsedModel);
        Assert.IsTrue(result.Duration > TimeSpan.Zero);
        Assert.AreEqual(0, result.ToolCallCount);
    }

    [TestMethod]
    public async Task ExecuteAsync_CustomPreset_UsesSpecifiedPreset()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Done.");

        string? resolvedPreset = null;
        var executor = new SubAgentExecutor(
            new AgentLoop(),
            (presetName, _) =>
            {
                resolvedPreset = presetName;
                return Task.FromResult<IAiProvider>(provider);
            },
            (_, _, _) => Task.FromResult<IReadOnlyList<IAssistTool>>([]));

        var request = new SubAgentRequest
        {
            Prompt = "Do something",
            ModelName = "custom-ollama",
        };

        var result = await executor.ExecuteAsync(request);

        Assert.AreEqual("custom-ollama", resolvedPreset);
        Assert.AreEqual("custom-ollama", result.UsedModel);
    }

    #endregion

    #region ContextHints

    [TestMethod]
    public async Task ExecuteAsync_WithKbIdHint_InjectsRagPrompt()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Report.");

        var executor = CreateExecutor(provider);

        var request = new SubAgentRequest
        {
            Prompt = "Search the knowledge base",
            ContextHints = new Dictionary<string, string>
            {
                [ContextHintKeys.KbId] = "kb-abc-123"
            },
        };

        await executor.ExecuteAsync(request);

        // Verify system prompt contains kb_id hint
        Assert.AreEqual(1, provider.ReceivedRequests.Count);
        var systemPrompt = provider.ReceivedRequests[0].SystemPrompt!;
        Assert.IsTrue(systemPrompt.Contains("kb_id=\"kb-abc-123\""),
            $"System prompt should contain kb_id hint. Actual: {systemPrompt}");
        Assert.IsTrue(systemPrompt.Contains("search_documents"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WithoutContextHints_NoRagHint()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Report.");

        var executor = CreateExecutor(provider);

        var request = new SubAgentRequest
        {
            Prompt = "Do something",
        };

        await executor.ExecuteAsync(request);

        var systemPrompt = provider.ReceivedRequests[0].SystemPrompt!;
        Assert.IsFalse(systemPrompt.Contains("kb_id="));
    }

    #endregion

    #region Report Template

    [TestMethod]
    public async Task ExecuteAsync_DefaultReportTemplate_IncludedInSystemPrompt()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Report.");

        var executor = CreateExecutor(provider);

        var request = new SubAgentRequest { Prompt = "go" };
        await executor.ExecuteAsync(request);

        var systemPrompt = provider.ReceivedRequests[0].SystemPrompt!;
        Assert.IsTrue(systemPrompt.Contains("Conclusion"),
            "Default report template should include English template.");
    }

    [TestMethod]
    public async Task ExecuteAsync_CustomReportInstruction_OverridesDefault()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Report.");

        var executor = CreateExecutor(provider);

        var request = new SubAgentRequest
        {
            Prompt = "go",
            ReportInstruction = "Write a one-line summary.",
        };

        await executor.ExecuteAsync(request);

        var systemPrompt = provider.ReceivedRequests[0].SystemPrompt!;
        Assert.IsTrue(systemPrompt.Contains("one-line summary"));
        Assert.IsFalse(systemPrompt.Contains("결론"),
            "Custom report instruction should replace default template.");
    }

    #endregion

    #region Timeout

    [TestMethod]
    public async Task ExecuteAsync_Timeout_ReturnsTimedOut()
    {
        // Provider that delays long enough for timeout to trigger
        var slowProvider = new SlowMockProvider(delay: TimeSpan.FromSeconds(10));

        var executor = new SubAgentExecutor(
            new AgentLoop(),
            (_, _) => Task.FromResult<IAiProvider>(slowProvider),
            (_, _, _) => Task.FromResult<IReadOnlyList<IAssistTool>>([]));

        var request = new SubAgentRequest
        {
            Prompt = "go",
            Timeout = TimeSpan.FromMilliseconds(50),
        };

        var result = await executor.ExecuteAsync(request);

        Assert.AreEqual(SubAgentStatus.TimedOut, result.Status);
        Assert.IsTrue(result.Report.Contains("timed out"));
    }

    [TestMethod]
    public async Task ExecuteAsync_ExternalCancellation_Throws()
    {
        var provider = new MockProvider();
        provider.EnqueueResponse("Done.");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var executor = CreateExecutor(provider);
        var request = new SubAgentRequest { Prompt = "go" };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(request, cts.Token));
    }

    #endregion

    #region Error Handling

    [TestMethod]
    public async Task ExecuteAsync_ProviderResolverThrows_ReturnsFailed()
    {
        var executor = new SubAgentExecutor(
            new AgentLoop(),
            (string? _, CancellationToken _) => throw new InvalidOperationException("API key not found"),
            (_, _, _) => Task.FromResult<IReadOnlyList<IAssistTool>>([]));

        var request = new SubAgentRequest { Prompt = "go" };
        var result = await executor.ExecuteAsync(request);

        Assert.AreEqual(SubAgentStatus.Failed, result.Status);
        Assert.IsTrue(result.Report.Contains("API key not found"));
    }

    #endregion

    #region Helpers

    /// <summary>Creates a <see cref="SubAgentExecutor"/> wired to the given mock provider with no tools.</summary>
    static SubAgentExecutor CreateExecutor(MockProvider provider)
    {
        return new SubAgentExecutor(
            new AgentLoop(),
            (_, _) => Task.FromResult<IAiProvider>(provider),
            (_, _, _) => Task.FromResult<IReadOnlyList<IAssistTool>>([]));
    }

    #endregion
}
