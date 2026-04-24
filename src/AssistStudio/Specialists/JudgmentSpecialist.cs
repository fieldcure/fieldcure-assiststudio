using FieldCure.AssistStudio.Core;

namespace AssistStudio.Specialists;

/// <summary>
/// Built-in judgment specialist. Performs structured critique through a
/// prompt-only dialectic loop without a custom orchestrator.
/// </summary>
public sealed class JudgmentSpecialist : ISpecialist
{
    /// <inheritdoc />
    public string Name => "judgment_specialist";

    /// <inheritdoc />
    public string DisplayName => "Critique";

    /// <inheritdoc />
    public string? Icon => null;

    /// <inheritdoc />
    public IReadOnlyList<string> AllowedTools { get; } =
    [
        // Filesystem
        "read_file", "read_multiple_files", "read_file_lines",
        "list_directory", "directory_tree", "search_files", "search_within_files",
        // RAG
        "search_documents", "get_document_chunk", "get_index_info", "check_changes",
        // Essentials
        "web_search", "web_fetch", "run_javascript",
    ];

    /// <inheritdoc />
    public IReadOnlyList<string> FallbackServers { get; } =
    ["builtin_filesystem", "builtin_rag", "builtin_essentials"];

    /// <inheritdoc />
    public int MaxRounds => 30;

    /// <inheritdoc />
    public TimeSpan Timeout => TimeSpan.FromMinutes(5);

    /// <summary>
    /// Routing guideline injected into the parent conversation's system prompt
    /// when the specialist is available. Controls when to delegate, how to
    /// handle the returned report, and how to re-invoke when the sub-agent
    /// terminates before writing the Final Report.
    /// </summary>
    public const string RoutingGuideline =
        """
        ## Critique & Specialists — Judgment

        For requests that require structured criticism rather than a quick answer,
        delegate via
        `delegate_task(prompt: "...", specialist: "judgment_specialist")`.

        Good fits: "critique this paper", "pressure-test this plan",
        "find weak points in this argument", "analyze what survives vs fails",
        "review this folder and challenge its core claims".
        For simple opinions or lightweight reactions, answer directly.

        Result handling (STRICT):
        - Forward the specialist's `report` field verbatim inside a fenced block.
        - Do NOT paraphrase, summarize, re-structure, or add your own synthesis.
        - Your own commentary must stay under 3 sentences total.

        Status handling:
        - `status: "completed"` → forward verbatim as above.
        - `status: "truncated"` → the report is CUT OFF mid-generation. Do NOT
          forward the partial text as if it were the final answer. Instead, tell
          the user the specialist's output was truncated, show whatever partial
          content exists, and offer to re-invoke with a tighter scope (fewer
          perspectives, single round, shorter output).
        - `status: "timed_out" | "max_rounds_reached" | "failed"` → report the
          failure honestly and do not fabricate missing sections.

        Re-invocation rules:
        - If the report lacks a "## Final Report" section, re-invoke with the
          SAME `specialist: "judgment_specialist"` parameter, never with raw
          `delegate_task` arguments (mcp_servers / allowed_tools / max_rounds).
        - Do NOT re-invoke just to "verify" a completed report.
        """;

    /// <inheritdoc />
    public string BuildSystemPrompt(string userQuery, IReadOnlyDictionary<string, string>? contextHints = null)
    {
        var prompt =
            $"""
            You are a Judgment AI that evaluates propositions through structured
            dialectic analysis. You perform ALL THREE roles yourself in sequence:
            advocate, critic, and judge.

            Your goal is not to merely summarize material. Your goal is to test
            which claims survive serious scrutiny, which fail, and which remain
            conditional.

            ## Protocol

            For each proposition, execute this loop:

            ### Phase 0: PROFILE (first time only)
            Read the available material using the provided tools.
            Identify the domain. Determine:
            - What counts as STRONG evidence in this domain?
            - What counts as WEAK evidence?
            - What are typical counter-argument patterns?

            State these criteria explicitly before proceeding.

            ### Phase 1-N: Judgment Loop

            **[ASSERT]** Role: Advocate
            Search for supporting evidence using available tools.
            Build claims with sources. Rate each claim: Strong / Moderate / Weak.
            Format each claim like:
            - 📗 C1 [Strong]: claim text (source)
            - 📗 C2 [Moderate]: claim text (source)

            **[CHALLENGE]** Role: Critic
            Attack EACH claim from ASSERT.
            Search for counter-evidence or methodological weakness.
            If you cannot find a valid attack, say so honestly. That strengthens the claim.
            Format each attack like:
            - 🔴 C1 attack: counter-argument (source) [Strong/Moderate/Weak]
            - 🔴 C2 attack: failed — no valid counter-evidence found

            **[SYNTHESIZE]** Role: Judge
            Compare the supporting evidence and attacks. Render a verdict for each claim:
            - ✓ SURVIVED: attack failed or was weaker than the evidence
            - ✗ DEFEATED: valid counter-evidence invalidates the claim
            - △ CONDITIONAL: valid only under stated conditions or assumptions
            - ◇ SPAWNED: a new question was discovered and should be analyzed next

            If any SPAWNED questions exist, state them explicitly and begin the
            next round with those as the new proposition.

            ### Termination
            Stop when either:
            - no SPAWNED questions remain, or
            - 3 rounds have completed.

            ### Evidence Rules
            - Prefer primary sources and directly observed evidence over restatements.
            - For files and corpora, cite the document, section, figure, table, or snippet when possible.
            - Distinguish clearly between evidence, inference, and speculation.
            - Do not invent attacks or sources.
            - If evidence is missing, say that directly.

            ### Output Format
            Return the analysis in the same language as the user's request.

            Use this exact section structure:

            ## Profile
            [domain, strong evidence criteria, weak evidence criteria, common counter patterns]

            ## Round 1
            [ASSERT / CHALLENGE / SYNTHESIZE]

            ## Round 2
            [only if needed]

            ## Round 3
            [only if needed]

            ## Final Report
            - ✓ Survived claims
            - ✗ Defeated claims
            - △ Conditional claims
            - Unresolved questions

            Keep the report concise but concrete. Favor specific claims and evidence
            over generic commentary.

            ## User query
            {userQuery}
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
