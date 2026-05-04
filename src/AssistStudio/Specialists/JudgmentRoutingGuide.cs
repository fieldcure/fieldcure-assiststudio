using AssistStudio.Tools;

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

        **All triggers below presuppose the artifact already exists in the
        conversation.** These specialists evaluate; they do not generate.

        | Trigger intent | specialist param | Purpose |
        |----------------|------------------|---------|
        | "critique this", "review this", "what's wrong with X" | {{CritiqueSpecialist.SpecialistName}} | Strengths/weaknesses + actionable fixes |
        | "red team this", "find weak points", "attack this" | {{RedTeamSpecialist.SpecialistName}} | Attack only, no fixes |
        | "play devil's advocate", "argue both sides", "for vs against" | {{DevilsAdvocateSpecialist.SpecialistName}} | For/against comparison, no recommendation |

        Match the user's intent regardless of language — the trigger column lists
        the English form, but equivalent phrases in any language route the same way.

        Usage: `{{SubAgentTool.ToolName}}(prompt: "user's request", specialist: "<id>")`

        If the user's intent is ambiguous, default to `{{CritiqueSpecialist.SpecialistName}}` (most common).

        ### Scope: evaluation-only

        These specialists assess **existing** artifacts (code, prose, designs).
        Do NOT delegate net-new generation or implementation work to them — e.g.,
        "build X", "write the code", "create a component", "make a Y". They will
        return a critique-shaped report instead of the artifact, and any code
        embedded in that report is illustrative, not a deliverable.

        Generate the artifact yourself first. Delegating the result for review is
        **optional and only when the user explicitly requests evaluation**.

        ### Examples of misrouting (do NOT do this)

        - User: "Build a QR code generator as a JSX artifact"
          → ✗ delegate to {{CritiqueSpecialist.SpecialistName}}. ✓ Generate the artifact yourself.
        - User: "Review how I should write this function" (no function shown yet)
          → ✗ delegate. ✓ Propose your design, then optionally critique it
            only if the user asks.
        - User: "Review this function" (function shown earlier in conversation)
          → ✓ delegate to {{CritiqueSpecialist.SpecialistName}}.
        - User: "Red team this PR" (PR diff present)
          → ✓ delegate to {{RedTeamSpecialist.SpecialistName}}.

        ## Delegation rules

        Forward the user's request to {{SubAgentTool.ToolName}} with MINIMAL
        transformation:
        - Pass the source material verbatim
        - Do NOT enumerate sub-areas or analysis dimensions
        - Do NOT restate the specialist's own protocol or constraints
        - Do NOT instruct {{DevilsAdvocateSpecialist.SpecialistName}} to argue only
          one side — its job is presenting both sides, and overriding that defeats
          the purpose

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

        ## Result handling (applies to ALL specialists above)

        - Forward the specialist's report verbatim. Do NOT paraphrase or
          re-structure.
        - Your own commentary must stay under 3 sentences.
        - If status is "truncated" or the report lacks a final section, inform
          the user and offer to re-invoke with **tighter scope** (e.g., narrower
          source material, more focused question). Do NOT silently retry with
          identical arguments — it will likely truncate again.
        - When re-invoking after a truncated result, use the SAME specialist
          parameter — never bypass to raw {{SubAgentTool.ToolName}} arguments.
        """;
}
