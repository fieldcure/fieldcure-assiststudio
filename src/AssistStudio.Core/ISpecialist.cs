namespace FieldCure.AssistStudio.Core;

/// <summary>
/// Defines a built-in specialist that runs as an auto-approved Sub-Agent.
/// Specialists declare only tool names (<see cref="AllowedTools"/>), not servers —
/// the harness resolves which servers provide those tools at runtime,
/// allowing external MCP servers to transparently replace built-in ones.
/// </summary>
/// <remarks>
/// <para><b>Self-containment principle.</b></para>
/// <para>
/// A specialist is responsible for declaring every tool and server it needs to
/// fulfill its role. The sub-agent harness merges <see cref="FallbackServers"/>
/// with the parent profile's enabled servers as a UNION (never a restriction),
/// so the specialist is guaranteed its declared tool surface regardless of
/// which profile invoked it.
/// </para>
/// <para>
/// This isolation is intentional: it makes specialist behavior reproducible
/// across parent profiles (Chat / General / Knowledge Base / etc.) and lets
/// evaluation against a ground-truth expectation stay meaningful. Routing
/// guideline injection and tool-availability gating MUST therefore be
/// independent of the parent profile's server selection — gating a specialist
/// on, say, the parent having Essentials enabled would silently degrade the
/// specialist's quality and break the contract.
/// </para>
/// <para>
/// If a specialist truly does not need a particular built-in (e.g. a pure
/// reasoning specialist that never reads files), omit it from
/// <see cref="FallbackServers"/> rather than gating the routing guideline
/// on the parent profile.
/// </para>
/// </remarks>
public interface ISpecialist
{
    /// <summary>
    /// Unique identifier (e.g. "web_search_specialist").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable display name (e.g. "Web Search Specialist").
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Display icon (emoji or Segoe Fluent Icons glyph). May be null.
    /// </summary>
    string? Icon { get; }

    /// <summary>
    /// Tool names the specialist requires. Only these tools are available.
    /// The harness resolves which servers provide them using parent conversation
    /// servers + <see cref="FallbackServers"/>.
    /// </summary>
    IReadOnlyList<string> AllowedTools { get; }

    /// <summary>
    /// Server IDs to connect if parent conversation servers don't provide
    /// the required tools. Ensures minimum functionality even when the parent
    /// profile has the relevant server disabled.
    /// </summary>
    IReadOnlyList<string> FallbackServers { get; }

    /// <summary>
    /// Maximum number of agent loop rounds.
    /// </summary>
    int MaxRounds { get; }

    /// <summary>
    /// Execution timeout.
    /// </summary>
    TimeSpan Timeout { get; }

    /// <summary>
    /// Builds the specialist's system prompt for the given user query.
    /// </summary>
    string BuildSystemPrompt(string userQuery, IReadOnlyDictionary<string, string>? contextHints = null);

    /// <summary>
    /// The literal markdown heading the report MUST start with (e.g.
    /// <c>"## Final Report"</c>). When non-null, the harness post-processes
    /// the sub-agent's report and strips any preamble (transitional sentences,
    /// internal phase headings, horizontal rules) appearing before this
    /// heading. Small models often leak chain-of-thought ("Now let me…",
    /// "## PHASE 3: …") despite explicit prompt instructions; this is the
    /// deterministic backstop. Return <see langword="null"/> for specialists
    /// whose first heading varies by language or is otherwise dynamic.
    /// </summary>
    string? ExpectedFirstHeading => null;

    /// <summary>
    /// Optional trailing section headings that should be stripped from this
    /// specialist's report if they appear as the final section(s).
    /// <para>
    /// Used to enforce specialist contracts where certain sections are
    /// forbidden by the specialist's role — e.g., RedTeam's "pure offense"
    /// rule prohibits remediation/fix sections, but small models occasionally
    /// produce them despite system prompt instructions.
    /// </para>
    /// <para>
    /// When null or empty, no trailing strip is applied. When provided, the
    /// harness searches for any of these headings as a top-level (##) or
    /// second-level (###) heading anchored to end-of-line. If found, the
    /// report is truncated at the heading's position. The match is
    /// case-insensitive.
    /// </para>
    /// <para>
    /// Conservative matching: only matches as standalone section headings,
    /// not as inline mentions or bullet text. Headings within the body of
    /// preceding sections are not affected.
    /// </para>
    /// </summary>
    IReadOnlyList<string>? ForbiddenTrailingHeadings => null;
}
