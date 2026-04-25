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
    public static readonly string RoutingGuideline =
        $$"""
        ## Specialists — Judgment Category

        | Trigger intent | specialist param | Purpose |
        |----------------|-----------------|---------|
        | "critique this", "review this", "what's wrong with X" | {{CritiqueSpecialist.SpecialistName}} | Strengths/weaknesses + actionable fixes |
        | "red team this", "find weak points", "attack this" | {{RedTeamSpecialist.SpecialistName}} | Attack only, no fixes |
        | "play devil's advocate", "argue both sides", "for vs against" | {{DevilsAdvocateSpecialist.SpecialistName}} | For/against comparison, no recommendation |

        Match the user's intent regardless of language — the trigger column lists
        the English form, but equivalent phrases in any language route the same way.

        Usage: `delegate_task(prompt: "user's request", specialist: "<id>")`

        If the user's intent is ambiguous, default to `{{CritiqueSpecialist.SpecialistName}}` (most common).

        ## Delegation rules

        Forward the user's request to delegate_task with MINIMAL transformation:
        - Pass the source material verbatim
        - Do NOT enumerate sub-areas or analysis dimensions
        - Do NOT restate the specialist's own protocol or constraints
        - Do NOT instruct {{DevilsAdvocateSpecialist.SpecialistName}} to argue only one side — its job
          is presenting both sides, and overriding that defeats the purpose

        The specialist's system prompt already contains the protocol. Your
        additions only amplify token cost or distort the specialist's role.

        Examples of what NOT to add to the specialist prompt:
        - "Focus on these areas: ..."
        - "Cover the following dimensions: ..."
        - Bullet lists of sub-topics
        - "Be specific about ..."
        - Restating the specialist's own protocol

        The user's raw request + the source material is sufficient. The
        specialist knows its job.

        Result handling (applies to ALL specialists above):
        - Forward the specialist's report verbatim. Do NOT paraphrase or re-structure.
        - Your own commentary must stay under 3 sentences.
        - If status is "truncated", inform the user and offer to re-invoke with
          tighter scope.
        - If the report lacks a final section, re-invoke with the SAME specialist
          parameter — never with raw delegate_task arguments.
        """;
}
