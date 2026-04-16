using System.Text.Encodings.Web;
using System.Text.Json;
using AssistStudio.Specialists;
using FieldCure.Ai.Execution;
using FieldCure.Ai.Execution.Models;
using FieldCure.Ai.Providers.Models;

namespace AssistStudio.Tools;

/// <summary>
/// Tool that delegates a task to an isolated sub-agent.
/// The sub-agent runs in a separate LLM session with its own tools
/// and returns a report to the parent conversation.
/// When <c>specialist</c> is specified and registered in <see cref="SpecialistRegistry"/>,
/// the call is auto-approved and uses the specialist's predefined configuration.
/// </summary>
public sealed class SubAgentTool : IAssistTool
{
    #region Fields

    private readonly ISubAgentExecutor _executor;
    private readonly Func<string?> _kbIdProvider;
    private readonly SpecialistRegistry _registry;
    private readonly Func<string?> _specialistPresetProvider;
    private readonly Func<string?> _subAgentPresetProvider;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="SubAgentTool"/> class.
    /// </summary>
    /// <param name="executor">Sub-agent executor for isolated LLM sessions.</param>
    /// <param name="kbIdProvider">
    /// Returns the current conversation's Knowledge Base folder (kb_id),
    /// or <c>null</c> if no KB is selected.
    /// </param>
    /// <param name="registry">Registry of built-in specialists for auto-approve lookup.</param>
    /// <param name="specialistPresetProvider">
    /// Returns the preferred provider preset for specialist execution,
    /// or <c>null</c> to inherit the parent conversation's provider.
    /// </param>
    /// <param name="subAgentPresetProvider">
    /// Returns the per-task preset for standard sub-agent execution
    /// (from App Tasks > Sub-Agent setting), or <c>null</c> for "Inherit".
    /// </param>
    public SubAgentTool(
        ISubAgentExecutor executor,
        Func<string?> kbIdProvider,
        SpecialistRegistry registry,
        Func<string?> specialistPresetProvider,
        Func<string?> subAgentPresetProvider)
    {
        _executor = executor;
        _kbIdProvider = kbIdProvider;
        _registry = registry;
        _specialistPresetProvider = specialistPresetProvider;
        _subAgentPresetProvider = subAgentPresetProvider;
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
        "Specify which MCP servers and tools the sub-agent needs, or use a built-in specialist.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "prompt": {
              "type": "string",
              "description": "Task description for the sub-agent. Be specific about what to do and what to report."
            },
            "specialist": {
              "type": "string",
              "description": "Built-in specialist name (e.g. 'web_search_specialist'). When set, uses specialist's predefined config (prompt, tools, timeout). Other params except prompt are ignored."
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

        // Specialist path: auto-approved, uses specialist's predefined config
        var specialistName = parameters.TryGetProperty("specialist", out var sp)
            ? sp.GetString() : null;

        if (specialistName is not null && _registry.TryGet(specialistName, out var specialist))
        {
            var contextHints = BuildContextHints(specialist.FallbackServers, specialist.AllowedTools);

            var request = new SubAgentRequest
            {
                Prompt = specialist.BuildSystemPrompt(prompt, contextHints),
                // Specialist: use specialist-specific preset, or null to inherit parent.
                // Per-task Sub-Agent setting is NOT applied here.
                PresetName = _specialistPresetProvider(),
                McpServers = specialist.FallbackServers.ToList(),
                AllowedTools = specialist.AllowedTools.ToList(),
                MaxRounds = specialist.MaxRounds,
                Timeout = specialist.Timeout,
                ContextHints = contextHints,
            };

            var result = await _executor.ExecuteAsync(request, ct);
            return FormatResult(result);
        }

        // Standard sub-agent path: existing logic unchanged
        var mcpServers = ParseStringArray(parameters, "mcp_servers");
        var allowedTools = ParseStringArray(parameters, "allowed_tools");

        var maxRounds = parameters.TryGetProperty("max_rounds", out var mr)
            ? mr.GetInt32() : 10;

        var timeoutSeconds = parameters.TryGetProperty("timeout_seconds", out var ts)
            ? ts.GetInt32() : 120;

        var reportInstruction = parameters.TryGetProperty("report_instruction", out var ri)
            ? ri.GetString() : null;

        // Auto-propagate ContextHints
        var standardContextHints = BuildContextHints(mcpServers, allowedTools);

        var standardRequest = new SubAgentRequest
        {
            Prompt = prompt,
            // Standard sub-agent: LLM-specified preset takes priority,
            // then per-task setting, then null (inherit parent).
            PresetName = presetName ?? _subAgentPresetProvider(),
            McpServers = mcpServers,
            AllowedTools = allowedTools,
            MaxRounds = maxRounds,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            ReportInstruction = reportInstruction,
            ContextHints = standardContextHints,
        };

        var standardResult = await _executor.ExecuteAsync(standardRequest, ct);
        return FormatResult(standardResult);
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Builds ContextHints from the current conversation state.
    /// Injects kb_id when RAG tools will be available — either via explicit mcp_servers
    /// containing "rag", or via allowed_tools containing "search_documents"
    /// (tools may be inherited from parent servers even without explicit mcp_servers).
    /// </summary>
    private Dictionary<string, string>? BuildContextHints(
        IReadOnlyList<string>? mcpServers, IReadOnlyList<string>? allowedTools)
    {
        var hasRag = mcpServers?.Any(s =>
            s.Contains("rag", StringComparison.OrdinalIgnoreCase)) == true;

        var hasRagTool = allowedTools?.Any(t =>
            t.Equals("search_documents", StringComparison.OrdinalIgnoreCase)) == true;

        if (!hasRag && !hasRagTool)
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
        }, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    #endregion
}
