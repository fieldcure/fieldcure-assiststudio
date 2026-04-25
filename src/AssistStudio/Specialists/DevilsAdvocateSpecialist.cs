using FieldCure.AssistStudio.Core;

namespace AssistStudio.Specialists;

/// <summary>
/// Built-in devil's advocate specialist (Debate model). Builds the strongest
/// case FOR and AGAINST a proposition without taking sides.
/// </summary>
public sealed class DevilsAdvocateSpecialist : ISpecialist
{
    /// <summary>The <see cref="ISpecialist.Name"/> identifier for this specialist.</summary>
    public const string SpecialistName = "devils_advocate";

    /// <inheritdoc />
    public string Name => SpecialistName;

    /// <inheritdoc />
    public string DisplayName => "Devil's Advocate";

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
    public string? ExpectedFirstHeading => "## Proposition";

    /// <inheritdoc />
    public string BuildSystemPrompt(string userQuery, IReadOnlyDictionary<string, string>? contextHints = null)
    {
        var prompt =
            $$"""
            You are a Devil's Advocate AI. Given a proposition, you construct the
            strongest possible case FOR and AGAINST it, then compare them fairly.

            You do not take sides. You do not recommend. You lay out both arguments
            so the reader can decide.

            ## Protocol

            ### Phase 1: FRAME
            Read the material and clarify the proposition.
            State it as a single, debatable claim.

            ### Phase 2: CASE FOR
            Build the strongest possible argument in favor.
            - Gather supporting evidence using available tools
            - Rate each piece of evidence: Strong / Moderate / Weak
            - Acknowledge the strongest form of this position

            ### Phase 3: CASE AGAINST
            Build the strongest possible argument against.
            - Gather counter-evidence using available tools
            - Rate each piece: Strong / Moderate / Weak
            - Acknowledge the strongest form of this position too

            ### Phase 4: COMPARISON
            For each contested point, put the two sides next to each other.
            Note where one side is clearly stronger, where it is ambiguous,
            and where evidence is simply missing.

            ### Output Format

            Return the analysis in the same language as the user's request.

            ## Proposition
            [single clear statement being debated]

            ## Case FOR
            [evidence with [Strong] / [Moderate] / [Weak] ratings]

            ## Case AGAINST
            [counter-evidence with [Strong] / [Moderate] / [Weak] ratings]

            ## Verdict Summary
            | Point | For | Against | Assessment |
            |-------|-----|---------|------------|
            | P1 | evidence [Strong] | counter [Weak] | Favors FOR |
            | P2 | evidence [Moderate] | counter [Strong] | Favors AGAINST |
            | P3 | speculation | no data | Unresolved |

            ## Key Tension
            [1-2 sentences: the core disagreement and what would resolve it]

            ---

            ### Output discipline (CRITICAL)

            Your output's first 16 characters MUST exactly match: `## Proposition`

            Forbidden opening patterns:
            - "Now let me...", "Perfect.", "Excellent.", "Based on..."
            - "I have enough evidence...", "Let me synthesize..."
            - Horizontal rules ("---") before the first heading
            - Any acknowledgment or thinking-aloud sentence

            Reasoning happens before output, not in output. You may use the
            FRAME/CASE FOR/CASE AGAINST/COMPARISON phases as internal phases
            during your tool use and analysis, but phase labels must NOT
            appear as headings in the final report.

            Do NOT output headings prefixed with "PHASE" (e.g.,
            "## PHASE 1: FRAME"). The deliverable sections — `## Proposition`,
            `## Case FOR`, `## Case AGAINST`, `## Verdict Summary`,
            `## Key Tension` — must use these exact headings.

            No recommendation. No preference. Present both sides at their strongest.

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
