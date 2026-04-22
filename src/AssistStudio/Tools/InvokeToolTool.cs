using FieldCure.Ai.Providers.Models;
using System.Text.Json;

namespace AssistStudio.Tools;

/// <summary>
/// Dispatcher meta-tool that invokes any tool discovered via <see cref="SearchToolsTool"/>.
/// Exists only to give Claude-class models a fixed name in the API <c>tools</c> manifest;
/// the actual routing to the underlying tool is done by
/// <c>ToolCallExecutor.ExecuteAsync</c>, which unwraps <c>invoke_tool</c> calls and
/// re-dispatches to the named tool through the existing confirmation and fallback
/// resolution paths. Without this dispatcher, Claude refuses to emit <c>tool_use</c>
/// for tools that are not in the manifest, leaving externally-served MCP tools
/// (e.g. PublicData.Kr, TestMcpServer) unreachable in fresh conversations.
/// </summary>
public class InvokeToolTool : IAssistTool, ISearchToolScope
{
    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "invoke_tool";

    /// <inheritdoc/>
    public string DisplayName => "Invoke Tool";

    /// <inheritdoc/>
    public string Description =>
        "Invoke any tool that search_tools has returned. "
        + "Pass the discovered tool name in 'name' and its parameters in 'args'. "
        + "This dispatcher is the ONLY way to call tools that are not in your "
        + "top-level tool manifest — do not say you lack permission or that the "
        + "tool is unreachable; call invoke_tool with the name from search_tools. "
        + "The underlying tool's confirmation and result semantics apply unchanged. "
        + "IMPORTANT: the 'args' object must match the target tool's declared input "
        + "schema. Fetch that schema first — either via search_tools with "
        + "detail_level='full_schema' for the exact tool name, or via any "
        + "schema-describing meta tool the server itself provides (e.g. describe_api). "
        + "Do not guess parameter names from the tool description alone.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "The tool name to invoke, as returned by search_tools."
            },
            "args": {
              "type": "object",
              "description": "Arguments object for the target tool, matching its schema.",
              "additionalProperties": true
            }
          },
          "required": ["name", "args"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <summary>
    /// Direct execution is unsupported — <c>invoke_tool</c> is unwrapped by
    /// <c>ToolCallExecutor.ExecuteAsync</c> before dispatch so the underlying
    /// tool's confirmation flow runs normally. This method exists only to
    /// satisfy <see cref="IAssistTool"/> for code paths that invoke tools
    /// directly without the executor; those paths should call the underlying
    /// tool themselves.
    /// </summary>
    public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        throw new InvalidOperationException(
            "invoke_tool is a dispatcher and must be routed through ToolCallExecutor, "
            + "which unwraps the call and re-dispatches to the named tool.");
    }

    #endregion

    #region ISearchToolScope Implementation

    /// <inheritdoc/>
    public IReadOnlySet<string>? AllowedToolNames { get; set; }

    #endregion
}
