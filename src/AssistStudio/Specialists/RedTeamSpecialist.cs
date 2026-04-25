using FieldCure.AssistStudio.Core;

namespace AssistStudio.Specialists;

/// <summary>
/// Built-in red team specialist (Attack model). Identifies vulnerabilities,
/// failure modes, and weaknesses without proposing fixes.
/// </summary>
public sealed class RedTeamSpecialist : ISpecialist
{
    /// <summary>The <see cref="ISpecialist.Name"/> identifier for this specialist.</summary>
    public const string SpecialistName = "red_team";

    /// <inheritdoc />
    public string Name => SpecialistName;

    /// <inheritdoc />
    public string DisplayName => "Red Team";

    /// <inheritdoc />
    public string? Icon => null;

    /// <inheritdoc />
    public IReadOnlyList<string> AllowedTools { get; } =
    [
        "read_file", "read_multiple_files", "read_file_lines",
        "list_directory", "directory_tree", "search_files", "search_within_files",
        "search_documents", "get_document_chunk", "get_index_info", "check_changes",
        "web_search", "web_fetch", "run_javascript",
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> FallbackServers { get; } =
    ["builtin_filesystem", "builtin_rag", "builtin_essentials"];

    /// <inheritdoc />
    public int MaxRounds => 30;

    /// <inheritdoc />
    public TimeSpan Timeout => TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public string? ExpectedFirstHeading => "## Threat Report";

    /// <summary>
    /// Trailing remediation section headings that violate RedTeam's
    /// "pure offense" contract. The specialist's system prompt forbids
    /// suggesting fixes, but small models (especially Claude Haiku)
    /// occasionally append these sections at report end despite explicit
    /// instructions. Conservative list — only patterns observed in actual
    /// evaluation runs (v4, v5). Expand only when new leak patterns are
    /// confirmed in subsequent evaluations.
    /// </summary>
    public IReadOnlyList<string>? ForbiddenTrailingHeadings { get; } =
    [
        "Immediate action items",
        "Action items",
        "Key fixes",
        "Recommendations",
        "Mitigations",
        "Remediation",
        "Next steps",
    ];

    /// <inheritdoc />
    public string BuildSystemPrompt(string userQuery, IReadOnlyDictionary<string, string>? contextHints = null)
    {
        var prompt =
            $$"""
            You are a Red Team AI. Your sole purpose is to find vulnerabilities,
            weaknesses, and failure modes. You do NOT suggest fixes — you attack.

            Think like an adversary: what would break this? What was overlooked?
            What assumption is fragile?

            ## Protocol

            ### Phase 1: RECONNAISSANCE
            Read the material using available tools.
            Identify the attack surface:
            - What claims are made?
            - What assumptions are implicit?
            - What edge cases are unaddressed?

            ### Phase 2: ATTACK
            For each attack vector, execute:
            - Describe the vulnerability
            - Provide evidence or a concrete scenario where it fails
            - Rate severity: Critical / High / Medium / Low
            - Rate exploitability: Easy / Moderate / Hard

            Use tools (web_search, rag_search, read_file) to find real counter-examples.
            Do NOT fabricate attacks. If something is solid, say so and move on.

            ### Phase 3: THREAT REPORT
            Compile findings by severity.

            ### Output Format

            Return the analysis in the same language as the user's request.

            ## Threat Report

            ### Critical
            - V1: [vulnerability] — [evidence/scenario] — Exploitability: Easy

            ### High
            - V2: [vulnerability] — [evidence/scenario] — Exploitability: Moderate

            ### Medium / Low
            - V3: ...

            ### Attack Surface Summary
            [2-3 sentences: overall exposure level, most dangerous vector,
            what the attacker would target first]

            ---

            ### Output discipline (CRITICAL)

            Your output's first 16 characters MUST exactly match: `## Threat Report`

            Forbidden opening patterns:
            - "Now let me...", "Perfect.", "Excellent.", "Based on..."
            - "I have enough evidence...", "Let me synthesize..."
            - Horizontal rules ("---") before the first heading
            - Any acknowledgment or thinking-aloud sentence

            Reasoning happens before output, not in output. You may use the
            RECONNAISSANCE/ATTACK/THREAT REPORT phases as internal phases during
            your tool use and analysis, but these phase labels must NOT appear
            as headings in the final report.

            Do NOT output headings containing "PHASE" or "RECONNAISSANCE".
            The deliverable section is listed in the format above.
            Keep each vulnerability on a single line.

            No fixes. No encouragement. Pure offense.

            ## User query
            {{userQuery}}
            """;

        if (contextHints is { Count: > 0 })
        {
            prompt += "\n\n## Context";
            foreach (var (key, value) in contextHints)
                prompt += $"\n- {key}: {value}";
        }

        return prompt;
    }
}
