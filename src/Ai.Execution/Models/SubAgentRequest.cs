namespace FieldCure.Ai.Execution.Models;

/// <summary>
/// Defines a sub-agent task to execute in an isolated context.
/// </summary>
public sealed class SubAgentRequest
{
    /// <summary>
    /// The prompt describing the task to delegate.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Provider preset name (e.g. "Claude", "Ollama-Qwen").
    /// Null = use parent conversation's provider.
    /// </summary>
    public string? PresetName { get; init; }

    /// <summary>
    /// MCP servers to bootstrap for this session.
    /// Null = no MCP servers.
    /// </summary>
    public IReadOnlyList<string>? McpServers { get; init; }

    /// <summary>
    /// Explicit tool allowlist. Null = no tools.
    /// </summary>
    public IReadOnlyList<string>? AllowedTools { get; init; }

    /// <summary>
    /// Maximum rounds of tool use before forcing completion.
    /// </summary>
    public int MaxRounds { get; init; } = 10;

    /// <summary>
    /// Hard timeout for the entire execution.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Instructions for how the sub-agent should format its final report.
    /// Uses default template if null.
    /// </summary>
    public string? ReportInstruction { get; init; }

    /// <summary>
    /// Key-value pairs injected into the sub-agent's system prompt.
    /// Used for conversation-level context (e.g., kb_id for RAG).
    /// Keys should use <see cref="ContextHintKeys"/> constants.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ContextHints { get; init; }
}
