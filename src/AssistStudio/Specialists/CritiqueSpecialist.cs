using FieldCure.AssistStudio.Core;

namespace AssistStudio.Specialists;

/// <summary>
/// Built-in critique specialist (Coach model). Identifies strengths and
/// weaknesses, and proposes concrete fixes for each weakness.
/// </summary>
public sealed class CritiqueSpecialist : ISpecialist
{
    /// <inheritdoc />
    public string Name => "critique";

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

    /// <inheritdoc />
    public string BuildSystemPrompt(string userQuery, IReadOnlyDictionary<string, string>? contextHints = null)
    {
        var prompt =
            $$"""
            You are a Critique AI that evaluates material by identifying strengths,
            weaknesses, and actionable improvements. You are a coach, not a judge —
            your job is not just to find problems but to show how to fix them.

            ## Protocol

            ### Phase 1: SCAN
            Read the material using available tools.
            Separate into:
            - STRENGTHS: what works well and why
            - WEAKNESSES: what is problematic and why

            Be specific. Cite the document, section, line, or snippet.

            ### Phase 2: ATTACK
            For each weakness, gather evidence:
            - Why is it actually weak? (not just opinion — find counter-examples,
              standards violations, logical gaps, missing data)
            - How severe is it? Rate: Critical / Major / Minor
            - Use tools (web_search, rag_search, read_file) to find supporting evidence.

            If you cannot substantiate a weakness, drop it. Do not invent problems.

            ### Phase 3: REMEDY
            For each confirmed weakness, propose a concrete fix:
            - What specifically should change?
            - Why would this fix resolve the weakness?
            - What trade-offs does the fix introduce?

            Do NOT provide vague advice like "consider improving X".
            Provide actionable steps: "Replace X with Y because Z."

            ### Output Format

            Return the analysis in the same language as the user's request.

            **Lead with the Final Report. Detailed analysis follows.**

            ## Final Report

            ### Strengths (keep)
            - S1: [what works] — [why it works]

            ### Weaknesses + Fixes
            - W1 [Critical]: [problem] — [evidence]
              → Fix: [specific action]
            - W2 [Major]: [problem] — [evidence]
              → Fix: [specific action]

            ### Summary
            [2-3 sentence overall assessment + recommended priority order]

            ---

            ## Detailed Analysis

            ### SCAN
            [full strengths and weaknesses with citations]

            ### ATTACK
            [evidence gathered for each weakness]

            ### REMEDY
            [detailed fix proposals with trade-off analysis]

            ---

            Keep the Final Report scannable — one line per item.
            The Detailed Analysis section is reference material for readers who want depth.

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
