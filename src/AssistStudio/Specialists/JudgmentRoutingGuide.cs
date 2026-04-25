namespace AssistStudio.Specialists;

/// <summary>
/// Shared routing guideline for judgment-category specialists
/// (critique, red_team, devils_advocate).
/// Injected into the parent conversation's system prompt when any
/// judgment specialist is registered.
/// </summary>
public static class JudgmentRoutingGuide
{
    /// <summary>
    /// The routing guideline text appended to the parent system prompt.
    /// </summary>
    public const string RoutingGuideline =
        """
        ## Specialists — Judgment Category

        | Trigger intent | specialist param | Purpose |
        |----------------|-----------------|---------|
        | "critique this", "review this", "what's wrong with X" | critique | Strengths/weaknesses + actionable fixes |
        | "red team this", "find weak points", "attack this" | red_team | Attack only, no fixes |
        | "play devil's advocate", "argue both sides", "for vs against" | devils_advocate | For/against comparison, no recommendation |

        Match the user's intent regardless of language — the trigger column lists
        the English form, but equivalent phrases in any language route the same way.

        Usage: `delegate_task(prompt: "user's request", specialist: "<id>")`

        If the user's intent is ambiguous, default to `critique` (most common).

        Result handling (applies to ALL specialists above):
        - Forward the specialist's report verbatim. Do NOT paraphrase or re-structure.
        - Your own commentary must stay under 3 sentences.
        - If status is "truncated", inform the user and offer to re-invoke with
          tighter scope.
        - If the report lacks a final section, re-invoke with the SAME specialist
          parameter — never with raw delegate_task arguments.
        """;
}
