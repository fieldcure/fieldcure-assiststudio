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
    /// Receives the tool name and arguments JSON string. Returns a tuple of
    /// (Approved, UserNote) where UserNote is an optional instruction from the user.
    /// </summary>
    public Func<string, string, Task<(bool Approved, string? UserNote)>>? ConfirmationHandler { get; set; }

    /// <summary>
    /// The user note from the most recent tool approval, or <c>null</c> if none was provided.
    /// Reset after each <see cref="ExecuteAsync"/> call. The caller should inject this as a
    /// user message in the next API request rather than appending to tool results.
    /// </summary>
    public string? LastUserNote { get; private set; }

    /// <summary>
    /// Optional fallback resolver invoked when a tool is not found in the primary tool list.
    /// Used by search_tools dynamic promotion: tools discovered at runtime can be resolved
    /// from connected MCP servers without rebuilding the executor.
    /// </summary>
    public Func<string, IAssistTool?>? FallbackToolResolver { get; set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// Executes a single tool call and returns a <see cref="ToolExecutionResult"/>
    /// containing the text result and optional multimedia content.
    /// </summary>
    /// <remarks>
    /// When the tool implements <see cref="IMultiContentTool"/>, the richer
    /// <see cref="IMultiContentTool.ExecuteWithContentAsync"/> path is used
    /// to preserve image data from MCP tool results.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when the requested tool is not found.</exception>
    public async Task<ToolExecutionResult> ExecuteAsync(ToolCall call, CancellationToken ct = default)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == call.FunctionName)
            ?? FallbackToolResolver?.Invoke(call.FunctionName)
            ?? throw new InvalidOperationException($"Tool not found: {call.FunctionName}");

        LastUserNote = null;
        if (tool.RequiresConfirmation && ConfirmationHandler is not null)
        {
            var (approved, note) = await ConfirmationHandler(tool.Name, call.Arguments);
            if (!approved)
            {
                var reason = string.IsNullOrWhiteSpace(note)
                    ? "Tool call rejected by user."
                    : $"Tool call rejected by user. Reason: {note}";
                DiagnosticLogger.LogInfo($"[Tool] Rejected: {tool.Name} — {reason}");
                return new ToolExecutionResult(reason);
            }
            LastUserNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        }

        var argsJson = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments;
        var args = JsonSerializer.Deserialize<JsonElement>(argsJson);

        DiagnosticLogger.LogInfo($"[Tool] Calling: {tool.Name} args={call.Arguments}");

        // Run tool execution on a thread pool thread to avoid blocking the UI thread.
        // ConfirmationHandler (above) has already run on the caller's context.
        ToolExecutionResult result;
        if (tool is IMultiContentTool multiContentTool)
        {
            result = await Task.Run(() => multiContentTool.ExecuteWithContentAsync(args, ct), ct);
        }
        else
        {
            var text = await Task.Run(() => tool.ExecuteAsync(args, ct), ct);
            result = new ToolExecutionResult(text);
        }

        DiagnosticLogger.LogInfo($"[Tool] Result: {tool.Name} → {Truncate(result.Text, 500)}");

        return result;
    }

    /// <summary>
    /// Executes a tool call without the confirmation check, for cases where
    /// confirmation has already been obtained externally (e.g., parallel sub-agent flow).
    /// </summary>
    public async Task<ToolExecutionResult> ExecuteWithoutConfirmationAsync(
        ToolCall call, string? userNote, CancellationToken ct = default)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == call.FunctionName)
            ?? FallbackToolResolver?.Invoke(call.FunctionName)
            ?? throw new InvalidOperationException($"Tool not found: {call.FunctionName}");

        LastUserNote = string.IsNullOrWhiteSpace(userNote) ? null : userNote?.Trim();

        var argsJson = string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments;
        var args = JsonSerializer.Deserialize<JsonElement>(argsJson);

        DiagnosticLogger.LogInfo($"[Tool] Calling (no-confirm): {tool.Name} args={call.Arguments}");

        ToolExecutionResult result;
        if (tool is IMultiContentTool multiContentTool)
        {
            result = await Task.Run(() => multiContentTool.ExecuteWithContentAsync(args, ct), ct);
        }
        else
        {
            var text = await Task.Run(() => tool.ExecuteAsync(args, ct), ct);
            result = new ToolExecutionResult(text);
        }

        DiagnosticLogger.LogInfo($"[Tool] Result (no-confirm): {tool.Name} → {Truncate(result.Text, 500)}");

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
