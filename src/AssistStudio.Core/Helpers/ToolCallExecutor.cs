using System.Text.Json;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Orchestrates the execution of tool calls requested by an AI model.
/// </summary>
public class ToolCallExecutor
{
    #region Fields

    /// <summary>The registered tools available for execution.</summary>
    private readonly IReadOnlyList<IAssistTool> _tools;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="ToolCallExecutor"/> with the given set of tools.
    /// </summary>
    public ToolCallExecutor(IReadOnlyList<IAssistTool> tools)
    {
        _tools = tools;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Callback invoked when a tool requires user confirmation before execution.
    /// Receives the tool name and arguments JSON string. Return <see langword="true"/> to proceed, <see langword="false"/> to skip.
    /// </summary>
    public Func<string, string, Task<bool>>? ConfirmationHandler { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Executes a single tool call and returns the result string for the AI model.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the requested tool is not found.</exception>
    public async Task<string> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == call.FunctionName)
            ?? throw new InvalidOperationException($"Tool not found: {call.FunctionName}");

        if (tool.RequiresConfirmation && ConfirmationHandler is not null)
        {
            var approved = await ConfirmationHandler(tool.Name, call.Arguments);
            if (!approved)
            {
                DiagnosticLogger.LogInfo($"[Tool] Skipped (user declined): {tool.Name}");
                return JsonSerializer.Serialize(new { skipped = true, reason = "User declined" });
            }
        }

        var argsJson = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments;
        var args = JsonSerializer.Deserialize<JsonElement>(argsJson);

        DiagnosticLogger.LogInfo($"[Tool] Calling: {tool.Name} args={call.Arguments}");

        // Run tool execution on a thread pool thread to avoid blocking the UI thread.
        // ConfirmationHandler (above) has already run on the caller's context.
        var result = await Task.Run(() => tool.ExecuteAsync(args, ct), ct);

        DiagnosticLogger.LogInfo($"[Tool] Result: {tool.Name} → {Truncate(result, 500)}");

        return result;
    }

    #endregion

    #region Private Methods

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }

    #endregion
}
