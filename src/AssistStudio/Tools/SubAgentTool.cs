using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using AssistStudio.Helpers;
using AssistStudio.Specialists;
using FieldCure.AssistStudio.Core;
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
    private readonly Func<string, string?> _specialistPresetProvider;
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
    /// Resolves the preferred provider preset for the given specialist name,
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
        Func<string, string?> specialistPresetProvider,
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
                PresetName = _specialistPresetProvider(specialist.Name),
                McpServers = specialist.FallbackServers.ToList(),
                AllowedTools = specialist.AllowedTools.ToList(),
                MaxRounds = specialist.MaxRounds,
                Timeout = specialist.Timeout,
                ContextHints = contextHints,
            };

            var result = await _executor.ExecuteAsync(request, ct);
            result = NormalizeSpecialistReport(result, specialist);
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

    /// <summary>
    /// Normalizes a specialist's report by:
    /// <list type="number">
    /// <item>Stripping any preamble before
    /// <see cref="ISpecialist.ExpectedFirstHeading"/>.</item>
    /// <item>Stripping any forbidden trailing section per
    /// <see cref="ISpecialist.ForbiddenTrailingHeadings"/>.</item>
    /// </list>
    /// Both stages are independent opt-ins. A specialist can declare either,
    /// both, or neither. Logging records what was stripped for each stage to
    /// aid future prompt-vs-postprocess decisions.
    /// </summary>
    private static SubAgentResult NormalizeSpecialistReport(
        SubAgentResult result, ISpecialist specialist)
    {
        if (string.IsNullOrEmpty(result.Report))
            return result;

        var report = result.Report;
        report = StripLeadingPreamble(report, specialist);
        report = StripForbiddenTrailingSection(report, specialist);

        if (ReferenceEquals(report, result.Report))
            return result;

        return new SubAgentResult
        {
            Report = report,
            Status = result.Status,
            ToolCallCount = result.ToolCallCount,
            Duration = result.Duration,
            RoundsExecuted = result.RoundsExecuted,
            UsedPreset = result.UsedPreset,
        };
    }

    /// <summary>
    /// Strips leaked preamble (transitional sentences, internal phase headings,
    /// horizontal rules) before <see cref="ISpecialist.ExpectedFirstHeading"/>.
    /// Small models often ignore prompt-level discipline rules and emit
    /// "Now let me…", "## PHASE 3: …", or similar before the deliverable —
    /// this is the deterministic backstop. Returns the report unchanged when
    /// the specialist declares no expected heading or when the heading is not
    /// found (even via fuzzy match).
    /// </summary>
    private static string StripLeadingPreamble(string report, ISpecialist specialist)
    {
        var heading = specialist.ExpectedFirstHeading;
        if (string.IsNullOrEmpty(heading))
            return report;

        var idx = FindExpectedHeadingStart(report, heading);
        if (idx <= 0)
            return report;

        var preamble = report[..idx];
        var stripped = report[idx..].TrimStart('\n', '\r', ' ', '\t');

        // Sample the first 80 chars (single-line) of the stripped preamble
        // so we can later analyze whether leak patterns are predictable
        // enough to address at the prompt level.
        var sample = preamble.Replace('\n', ' ').Replace('\r', ' ').Trim();
        if (sample.Length > 80) sample = sample[..80] + "…";

        LoggingService.LogInfo(
            $"[Specialist:{specialist.Name}] Stripped {idx} chars before '{heading}'. " +
            $"Preamble: \"{sample}\"");

        return stripped;
    }

    /// <summary>
    /// Searches for any forbidden trailing heading and truncates the report at
    /// that position. Matches only top-level (##) or sub (###) headings
    /// anchored to end-of-line, case-insensitive.
    /// <para>
    /// If multiple forbidden headings appear, truncates at the EARLIEST
    /// occurrence — once a forbidden section starts, everything after it is
    /// suspect (often the model continues with more remediation content under
    /// different headings).
    /// </para>
    /// <para>
    /// Preserves the report unchanged if no match is found (safe fallback).
    /// </para>
    /// </summary>
    private static string StripForbiddenTrailingSection(string report, ISpecialist specialist)
    {
        var forbidden = specialist.ForbiddenTrailingHeadings;
        if (forbidden is null || forbidden.Count == 0)
            return report;

        var earliestMatchIndex = -1;
        string? matchedHeading = null;

        foreach (var heading in forbidden)
        {
            var pattern = $@"(^|\n)#{{2,3}}\s*{Regex.Escape(heading)}\s*(\r?\n|$)";
            var match = Regex.Match(report, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            var matchStart = match.Value.StartsWith('\n') ? match.Index + 1 : match.Index;
            if (earliestMatchIndex == -1 || matchStart < earliestMatchIndex)
            {
                earliestMatchIndex = matchStart;
                matchedHeading = heading;
            }
        }

        if (earliestMatchIndex == -1)
            return report;

        var trimmed = report[earliestMatchIndex..];
        var kept = report[..earliestMatchIndex].TrimEnd();
        var trimmedCount = report.Length - kept.Length;

        var sample = trimmed.Replace('\n', ' ').Replace('\r', ' ');
        if (sample.Length > 120) sample = sample[..120] + "…";

        LoggingService.LogInfo(
            $"[Specialist:{specialist.Name}] Trimmed trailing section '{matchedHeading}' " +
            $"({trimmedCount} chars). Content: \"{sample}\"");

        return kept;
    }

    /// <summary>
    /// Locates the expected first heading in the report, tolerating common
    /// formatting variations:
    /// <list type="bullet">
    /// <item>Heading levels: <c>#</c> vs <c>##</c> vs <c>###</c> (Markdown level mismatch)</item>
    /// <item>Case: "Final Report" vs "FINAL REPORT" vs "Final report"</item>
    /// <item>Whitespace: trailing/leading spaces, varied newlines</item>
    /// <item>Bold-instead-of-heading: <c>**Final Report**</c></item>
    /// </list>
    /// Returns the index where the heading line begins, or -1 if not found.
    /// Even when found via fuzzy match, the strip preserves the heading in
    /// its original form (does not rewrite to canonical).
    /// </summary>
    private static int FindExpectedHeadingStart(string report, string expectedHeading)
    {
        // Extract just the title text, e.g. "Final Report" from "## Final Report"
        var titleText = expectedHeading.TrimStart('#', ' ').Trim();
        if (string.IsNullOrEmpty(titleText))
            return -1;

        // (^|\n)  start of report or start of line
        // \s*     optional leading whitespace
        // (#{1,6}|\*\*)  one of: 1–6 hashes, or '**' (bold marker)
        // \s*     gap
        // {title} the literal title (case-insensitive)
        // \s*(\*\*)?  optional trailing bold close, optional whitespace
        // ($|\n)  end of report or end of line
        var pattern = $@"(^|\n)\s*(#{{1,6}}|\*\*)\s*{Regex.Escape(titleText)}\s*(\*\*)?\s*($|\n)";
        var match = Regex.Match(report, pattern, RegexOptions.IgnoreCase);
        if (!match.Success)
            return -1;

        // Skip a leading newline so the result starts at the heading line itself.
        return match.Value.StartsWith('\n') ? match.Index + 1 : match.Index;
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
            SubAgentStatus.Truncated => "truncated",
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
