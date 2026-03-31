namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Represents a tool invocation requested by an AI model.
/// </summary>
public class ToolCall
{
    #region Properties

    /// <summary>A unique identifier for this tool call, used to correlate results.</summary>
    public required string Id { get; init; }

    /// <summary>The name of the function/tool to invoke.</summary>
    public required string FunctionName { get; init; }

    /// <summary>The raw JSON string of arguments to pass to the tool.</summary>
    public required string Arguments { get; init; }

    #endregion
}
