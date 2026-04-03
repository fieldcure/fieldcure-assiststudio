using System.Diagnostics;
using FieldCure.Ai.Execution.Helpers;
using FieldCure.Ai.Execution.Models;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution;

/// <summary>
/// Creates an isolated LLM session for sub-agent execution.
/// Assembles the system prompt (with ContextHints), manages timeout,
/// and delegates the actual loop to <see cref="IAgentLoop"/>.
/// </summary>
public sealed class SubAgentExecutor : ISubAgentExecutor
{
    /// <summary>
    /// Resolves a provider preset name to an <see cref="IAiProvider"/> instance.
    /// Injected by the host (e.g., AssistStudio Core, Runner).
    /// </summary>
    public delegate IAiProvider ProviderResolver(string presetName);

    /// <summary>
    /// Resolves MCP server IDs and tool allowlist to a list of available tools.
    /// Injected by the host which manages MCP server lifecycle.
    /// Returns an empty list if no servers/tools are available.
    /// </summary>
    public delegate Task<IReadOnlyList<IAssistTool>> ToolResolver(
        IReadOnlyList<string>? mcpServers,
        IReadOnlyList<string>? allowedTools,
        CancellationToken cancellationToken);

    static readonly string DefaultReportTemplate =
        """
        작업이 끝나면 다음 형식으로 보고하세요:

        ## 결론
        (핵심 결론 한두 문장)

        ## 주요 발견
        (핵심 발견 사항 정리)

        ## 근거
        (출처, 데이터, 참조)

        ## 후속 작업
        (필요시 추가 조사/작업 제안)
        """;

    readonly IAgentLoop _agentLoop;
    readonly ProviderResolver _resolveProvider;
    readonly ToolResolver _resolveTools;
    readonly string _defaultPresetName;

    /// <summary>
    /// Creates a new sub-agent executor.
    /// </summary>
    /// <param name="agentLoop">Agent loop implementation to use.</param>
    /// <param name="resolveProvider">Resolves preset name → IAiProvider.</param>
    /// <param name="resolveTools">Resolves MCP servers + allowlist → tools.</param>
    /// <param name="defaultPresetName">
    /// Fallback preset when <see cref="SubAgentRequest.PresetName"/> is null
    /// (typically the parent conversation's current preset).
    /// </param>
    public SubAgentExecutor(
        IAgentLoop agentLoop,
        ProviderResolver resolveProvider,
        ToolResolver resolveTools,
        string defaultPresetName)
    {
        _agentLoop = agentLoop;
        _resolveProvider = resolveProvider;
        _resolveTools = resolveTools;
        _defaultPresetName = defaultPresetName;
    }

    /// <inheritdoc/>
    public async Task<SubAgentResult> ExecuteAsync(
        SubAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();

        // Timeout: SubAgentExecutor wraps with CancelAfter.
        // AgentLoop only sees the CancellationToken.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(request.Timeout);

        var presetName = request.PresetName ?? _defaultPresetName;

        try
        {
            // 1. Resolve provider
            var provider = _resolveProvider(presetName);

            // 2. Resolve tools
            var tools = await _resolveTools(
                request.McpServers, request.AllowedTools, timeoutCts.Token);

            // 3. Build system prompt: base + report template + context hints
            var systemPrompt = BuildSystemPrompt(request);

            // 4. Create context and run loop
            var context = new AgentLoopContext
            {
                Provider = provider,
                SystemPrompt = systemPrompt,
                UserPrompt = request.Prompt,
                Tools = tools.Count > 0 ? tools : null,
                MaxRounds = request.MaxRounds,
            };

            var loopResult = await _agentLoop.RunAsync(context, timeoutCts.Token);

            // 5. Map to SubAgentResult
            sw.Stop();
            return new SubAgentResult
            {
                Report = loopResult.Summary ?? loopResult.ErrorMessage ?? "(no report)",
                Status = MapStatus(loopResult.Status),
                ToolCallCount = loopResult.ToolCallCount,
                Duration = sw.Elapsed,
                RoundsExecuted = loopResult.RoundsExecuted,
                UsedPreset = presetName,
            };
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
                                                   && !cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            return new SubAgentResult
            {
                Report = $"Execution timed out after {request.Timeout.TotalSeconds:F0} seconds.",
                Status = SubAgentStatus.TimedOut,
                Duration = sw.Elapsed,
                UsedPreset = presetName,
            };
        }
        catch (OperationCanceledException)
        {
            throw; // User cancellation — propagate
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new SubAgentResult
            {
                Report = ex.Message,
                Status = SubAgentStatus.Failed,
                Duration = sw.Elapsed,
                UsedPreset = presetName,
            };
        }
    }

    #region Private Helpers

    static string BuildSystemPrompt(SubAgentRequest request)
    {
        var basePrompt =
            """
            You are an autonomous sub-agent running in an isolated context.
            Execute the given task using available tools, then write a report.
            Do not ask for human input — complete the task independently.
            """;

        var reportTemplate = request.ReportInstruction ?? DefaultReportTemplate;
        var hints = SystemPromptHints.BuildFromHints(request.ContextHints);

        return hints is not null
            ? $"{basePrompt}\n\n{reportTemplate}{hints}"
            : $"{basePrompt}\n\n{reportTemplate}";
    }

    static SubAgentStatus MapStatus(AgentLoopStatus loopStatus) => loopStatus switch
    {
        AgentLoopStatus.Completed => SubAgentStatus.Completed,
        AgentLoopStatus.MaxRoundsReached => SubAgentStatus.MaxRoundsReached,
        AgentLoopStatus.Failed => SubAgentStatus.Failed,
        _ => SubAgentStatus.Failed,
    };

    #endregion
}
