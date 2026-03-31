using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Resolves the final tool list at send time.
/// Combines built-in tools and MCP tools, handles name conflicts,
/// and filters by conversation state.
/// </summary>
public static class ToolResolver
{
    /// <summary>
    /// Resolves the active tool list for a request.
    /// Only tools enabled in the conversation state are included.
    /// </summary>
    /// <param name="builtInTools">All built-in tools (from ToolRegistry).</param>
    /// <param name="mcpTools">All MCP tools (from McpServerRegistry).</param>
    /// <param name="conversationState">Current conversation tool state.</param>
    /// <returns>Tools to send to the LLM.</returns>
    public static IReadOnlyList<IAssistTool> Resolve(
        IReadOnlyList<IAssistTool> builtInTools,
        IReadOnlyList<IAssistTool> mcpTools,
        ConversationToolState? conversationState)
    {
        var allTools = ResolveConflicts(builtInTools, mcpTools);

        if (conversationState is null)
            return allTools;

        return allTools
            .Where(t => conversationState.EnabledToolNames.Contains(t.Name))
            .ToList();
    }

    /// <summary>
    /// Gets all available tools for display in profile page or tools flyout.
    /// </summary>
    /// <param name="builtInTools">All built-in tools.</param>
    /// <param name="mcpTools">All MCP tools.</param>
    /// <returns>Combined and de-duplicated tool list.</returns>
    public static IReadOnlyList<IAssistTool> GetAllAvailable(
        IReadOnlyList<IAssistTool> builtInTools,
        IReadOnlyList<IAssistTool> mcpTools)
        => ResolveConflicts(builtInTools, mcpTools);

    /// <summary>
    /// Handles name conflicts between tools.
    /// Built-in tools take priority; MCP tools with conflicting names
    /// are prefixed with their server name.
    /// </summary>
    private static List<IAssistTool> ResolveConflicts(
        IReadOnlyList<IAssistTool> builtInTools,
        IReadOnlyList<IAssistTool> mcpTools)
    {
        var result = new List<IAssistTool>(builtInTools);
        var usedNames = new HashSet<string>(builtInTools.Select(t => t.Name));

        foreach (var tool in mcpTools)
        {
            if (usedNames.Contains(tool.Name))
            {
                if (tool is McpToolAdapter mcp && !string.IsNullOrEmpty(mcp.ServerName))
                {
                    // Create a prefixed adapter to avoid name collision
                    var prefixedName = $"{mcp.ServerName}/{mcp.Name}";
                    if (!usedNames.Contains(prefixedName))
                    {
                        var prefixed = new McpToolAdapter(
                            prefixedName,
                            mcp.Description,
                            mcp.ParameterSchema,
                            mcp.ExecuteAsync)
                        { ServerName = mcp.ServerName };

                        result.Add(prefixed);
                        usedNames.Add(prefixedName);
                    }
                }
                // Skip tools that still conflict after prefixing
                continue;
            }

            usedNames.Add(tool.Name);
            result.Add(tool);
        }

        return result;
    }
}
