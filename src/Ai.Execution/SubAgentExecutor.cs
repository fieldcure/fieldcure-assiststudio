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
    /// The resolver may perform async validation (e.g., connectivity check with fallback).
    /// </summary>
    public delegate Task<IAiProvider> ProviderResolver(string? presetName, CancellationToken cancellationToken);

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
        When the task is complete, report using this format:

        ## Conclusion
        (Key conclusion in one or two sentences)

        ## Key Findings
        (Summary of important findings)

        ## Evidence
        (Sources, data, references)

        ## Follow-up
        (Suggested next steps, if any)
        """;

    readonly IAgentLoop _agentLoop;
    readonly ProviderResolver _resolveProvider;
    readonly ToolResolver _resolveTools;

    /// <summary>
    /// Creates a new sub-agent executor.
    /// </summary>
    /// <param name="agentLoop">Agent loop implementation to use.</param>
    /// <param name="resolveProvider">
    /// Resolves preset name → IAiProvider. May perform async validation
    /// and fallback (e.g., via <c>IAuxiliaryProviderResolver</c>).
    /// Called with <see cref="SubAgentRequest.PresetName"/> which may be <see langword="null"/>
    /// if the caller intends the resolver to use its own fallback policy.
    /// </param>
    /// <param name="resolveTools">Resolves MCP servers + allowlist → tools.</param>
    public SubAgentExecutor(
        IAgentLoop agentLoop,
        ProviderResolver resolveProvider,
        ToolResolver resolveTools)
    {
        _agentLoop = agentLoop;
        _resolveProvider = resolveProvider;
        _resolveTools = resolveTools;
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

        var presetName = request.PresetName;

        try
        {
            // 1. Resolve provider (may perform async validation + fallback)
            var provider = await _resolveProvider(presetName, timeoutCts.Token);

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
