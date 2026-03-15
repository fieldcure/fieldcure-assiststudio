using System.Text.Json;

namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Defines a tool that can be invoked by an AI model during a conversation.
/// </summary>
public interface IAssistTool
{
    /// <summary>The tool name sent to the AI model (e.g. "scan_directory").</summary>
    string Name { get; }

    /// <summary>A description for the AI model to decide when to use this tool.</summary>
    string Description { get; }

    /// <summary>A JSON Schema string describing the tool's parameters.</summary>
    string ParameterSchema { get; }

    /// <summary>If <see langword="true"/>, the UI must show user confirmation before execution.</summary>
    bool RequiresConfirmation => false;

    /// <summary>
    /// Executes the tool with the given parameters and returns a result string for the AI model.
    /// </summary>
    Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default);
}
