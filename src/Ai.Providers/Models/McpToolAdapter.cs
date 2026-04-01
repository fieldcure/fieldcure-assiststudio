using System.Text.Json;

namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Adapts an MCP tool to the <see cref="IAssistTool"/> and <see cref="IMultiContentTool"/> interfaces.
/// Allows MCP tools to participate in the existing tool-calling pipeline
/// without any changes to <see cref="Providers.IAiProvider"/> implementations.
/// </summary>
/// <remarks>
/// The execute delegate is injected by the App layer, keeping Core free of
/// any MCP SDK dependency. The delegate typically captures an
/// <c>IMcpClient</c> instance to route calls to the MCP server.
/// </remarks>
public class McpToolAdapter : IMultiContentTool
{
    #region Fields

    private readonly Func<JsonElement, CancellationToken, Task<ToolExecutionResult>> _executeFunc;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="McpToolAdapter"/> class.
    /// </summary>
    /// <param name="name">The tool name sent to the AI model.</param>
    /// <param name="description">A description for the AI model.</param>
    /// <param name="parameterSchema">
    /// JSON Schema string describing the tool's parameters.
    /// Typically obtained via <c>JsonElement.GetRawText()</c> from the MCP SDK.
    /// </param>
    /// <param name="executeFunc">
    /// Delegate that invokes the MCP tool. Receives the parsed parameters
    /// and returns a <see cref="ToolExecutionResult"/> containing the text result
    /// and optional image data URIs.
    /// </param>
    public McpToolAdapter(
        string name,
        string description,
        string parameterSchema,
        Func<JsonElement, CancellationToken, Task<ToolExecutionResult>> executeFunc)
    {
        Name = name;
        Description = description;
        ParameterSchema = parameterSchema;
        _executeFunc = executeFunc;
    }

    #endregion

    #region IAssistTool

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string DisplayName => string.IsNullOrEmpty(ServerName)
        ? Name
        : $"{Name} ({ServerName})";

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public string ParameterSchema { get; }

    /// <inheritdoc />
    /// <remarks>
    /// External MCP tools always require user approval.
    /// Built-in server tools use <see cref="OverrideRequiresConfirmation"/>
    /// to allow read-only tools without approval.
    /// </remarks>
    public bool RequiresConfirmation => OverrideRequiresConfirmation ?? true;

    /// <inheritdoc />
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var result = await _executeFunc(parameters, ct);
        return result.Text;
    }

    #endregion

    #region IMultiContentTool

    /// <inheritdoc />
    public Task<ToolExecutionResult> ExecuteWithContentAsync(JsonElement parameters, CancellationToken ct = default)
        => _executeFunc(parameters, ct);

    #endregion

    #region Properties

    /// <summary>
    /// Gets the name of the MCP server this tool belongs to.
    /// Used for UI grouping and <c>ToolApprovalPanel</c> display.
    /// </summary>
    public string ServerName { get; init; } = "";

    /// <summary>
    /// Gets an optional override for <see cref="RequiresConfirmation"/>.
    /// When set, this value takes precedence over the default (<see langword="true"/>).
    /// Used by built-in servers to allow read-only tools without user approval.
    /// </summary>
    public bool? OverrideRequiresConfirmation { get; init; }

    #endregion
}
