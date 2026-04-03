using System.Text.Json;
using FieldCure.Ai.Execution;
using FieldCure.Ai.Execution.Models;
using FieldCure.Ai.Providers.Models;

namespace AssistStudio.Tools;

/// <summary>
/// Tool that delegates a task to an isolated sub-agent.
/// The sub-agent runs in a separate LLM session with its own tools
/// and returns a report to the parent conversation.
/// </summary>
public sealed class SubAgentTool : IAssistTool
{
    #region Fields

    private readonly ISubAgentExecutor _executor;
    private readonly Func<string?> _kbIdProvider;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="SubAgentTool"/> class.
    /// </summary>
    /// <param name="executor">Sub-agent executor for isolated LLM sessions.</param>
    /// <param name="kbIdProvider">
    /// Returns the current conversation's Knowledge Archive folder (kb_id),
    /// or <c>null</c> if no KB is selected.
    /// </param>
    public SubAgentTool(ISubAgentExecutor executor, Func<string?> kbIdProvider)
    {
        _executor = executor;
        _kbIdProvider = kbIdProvider;
    }

    #endregion

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "delegate_task";

    /// <inheritdoc/>
    public string DisplayName => "Sub-Agent";

    /// <inheritdoc/>
    public string Description =>
        "Delegate a task to an independent sub-agent that runs in an isolated context. " +
        "Use this when a task requires multiple tool calls (research, data processing, " +
        "file exploration, etc.). The sub-agent executes autonomously and returns a report. " +
        "Specify which MCP servers and tools the sub-agent needs.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "prompt": {
              "type": "string",
              "description": "Task description for the sub-agent. Be specific about what to do and what to report."
            },
            "preset_name": {
              "type": "string",
              "description": "Provider preset name to use (e.g. 'Claude', 'Ollama-Qwen'). Null = use current preset."
            },
            "mcp_servers": {
              "type": "array",
              "items": { "type": "string" },
              "description": "MCP server IDs to enable for this sub-agent (e.g. ['builtin_rag', 'builtin_essentials'])."
            },
            "allowed_tools": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Tool name allowlist. Only these tools will be available. Null = all tools from enabled servers."
            },
            "max_rounds": {
              "type": "integer",
              "description": "Maximum tool-use rounds before forcing completion. Default: 10."
            },
            "timeout_seconds": {
              "type": "integer",
              "description": "Hard timeout in seconds. Default: 120."
            },
            "report_instruction": {
              "type": "string",
              "description": "Custom instruction for how the sub-agent should format its final report."
            }
          },
          "required": ["prompt"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => true;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var prompt = parameters.GetProperty("prompt").GetString()
            ?? throw new ArgumentException("'prompt' is required.");

        var presetName = parameters.TryGetProperty("preset_name", out var pn)
            ? pn.GetString() : null;

        var mcpServers = ParseStringArray(parameters, "mcp_servers");
        var allowedTools = ParseStringArray(parameters, "allowed_tools");

        var maxRounds = parameters.TryGetProperty("max_rounds", out var mr)
            ? mr.GetInt32() : 10;

        var timeoutSeconds = parameters.TryGetProperty("timeout_seconds", out var ts)
            ? ts.GetInt32() : 120;

        var reportInstruction = parameters.TryGetProperty("report_instruction", out var ri)
            ? ri.GetString() : null;

        // Auto-propagate ContextHints
        var contextHints = BuildContextHints(mcpServers);

        var request = new SubAgentRequest
        {
            Prompt = prompt,
            PresetName = presetName,
            McpServers = mcpServers,
            AllowedTools = allowedTools,
            MaxRounds = maxRounds,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            ReportInstruction = reportInstruction,
            ContextHints = contextHints,
        };

        var result = await _executor.ExecuteAsync(request, ct);

        return FormatResult(result);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Builds ContextHints from the current conversation state.
    /// Automatically injects kb_id when RAG server is requested and a KB is selected.
    /// </summary>
    private Dictionary<string, string>? BuildContextHints(IReadOnlyList<string>? mcpServers)
    {
        if (mcpServers is null or { Count: 0 })
            return null;

        var hasRag = mcpServers.Any(s =>
            s.Contains("rag", StringComparison.OrdinalIgnoreCase));

        if (!hasRag)
            return null;

        var kbId = _kbIdProvider();
        if (string.IsNullOrEmpty(kbId))
            return null;

        return new Dictionary<string, string>
        {
            [ContextHintKeys.KbId] = kbId,
        };
    }

    private static IReadOnlyList<string>? ParseStringArray(JsonElement parameters, string propertyName)
    {
        if (!parameters.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            var value = item.GetString();
            if (!string.IsNullOrEmpty(value))
                list.Add(value);
        }

        return list.Count > 0 ? list : null;
    }

    private static string FormatResult(SubAgentResult result)
    {
        var statusText = result.Status switch
        {
            SubAgentStatus.Completed => "completed",
            SubAgentStatus.TimedOut => "timed_out",
            SubAgentStatus.MaxRoundsReached => "max_rounds_reached",
            SubAgentStatus.Failed => "failed",
            _ => "unknown",
        };

        return JsonSerializer.Serialize(new
        {
            status = statusText,
            report = result.Report,
            tool_call_count = result.ToolCallCount,
            rounds_executed = result.RoundsExecuted,
            duration_seconds = Math.Round(result.Duration.TotalSeconds, 1),
            used_preset = result.UsedPreset,
        }, new JsonSerializerOptions { WriteIndented = false });
    }

    #endregion
}
