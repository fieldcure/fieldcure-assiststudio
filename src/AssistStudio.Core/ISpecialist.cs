namespace FieldCure.AssistStudio.Core;

/// <summary>
/// Defines a built-in specialist that runs as an auto-approved Sub-Agent.
/// Specialists declare only tool names (<see cref="AllowedTools"/>), not servers —
/// the harness resolves which servers provide those tools at runtime,
/// allowing external MCP servers to transparently replace built-in ones.
/// </summary>
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
}
