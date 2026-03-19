using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using System.Text.Json;

namespace AssistStudio.Tools;

/// <summary>
/// Meta-tool that searches registered MCP tools by keyword.
/// Used when total tool count exceeds a threshold to avoid sending all tool
/// definitions in every request. The LLM discovers MCP tools on demand via this tool.
/// </summary>
public class SearchToolsTool : IAssistTool, ISearchToolScope
{
    #region Fields

    private readonly McpServerRegistry _registry;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchToolsTool"/> class.
    /// </summary>
    /// <param name="registry">The MCP server registry to search tools from.</param>
    public SearchToolsTool(McpServerRegistry registry)
    {
        _registry = registry;
    }

    #endregion

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "search_tools";

    /// <inheritdoc/>
    public string DisplayName => "Search Tools";

    /// <inheritdoc/>
    public string Description =>
        "Search available tools by keyword. Returns matching tool names and descriptions. " +
        "Call this before invoking any external tool you don't already know about.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Keywords to search for in tool names and descriptions"
            },
            "detail_level": {
              "type": "string",
              "enum": ["name_only", "summary", "full_schema"],
              "description": "Level of detail to return. Default: summary"
            }
          },
          "required": ["query"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var query = parameters.GetProperty("query").GetString() ?? "";
        var detail = parameters.TryGetProperty("detail_level", out var d)
            ? d.GetString() ?? "summary"
            : "summary";

        var keywords = query
            .ToLowerInvariant()
            .Split([' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries);

        var allTools = _registry.AllTools;
        var searchable = AllowedToolNames is null
            ? allTools
            : allTools.Where(t => AllowedToolNames.Contains(t.Name));

        var matched = searchable
            .Where(t => Match(t, keywords))
            .Select(t => FormatTool(t, detail))
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(matched));
    }

    #endregion

    #region ISearchToolScope Implementation

    /// <inheritdoc/>
    public IReadOnlySet<string>? AllowedToolNames { get; set; }

    #endregion

    #region Private Methods

    private static bool Match(IAssistTool tool, string[] keywords)
    {
        var target = $"{tool.Name} {tool.Description}".ToLowerInvariant();
        return keywords.Any(k => target.Contains(k));
    }

    private static object FormatTool(IAssistTool tool, string detail) => detail switch
    {
        "name_only" => new { tool.Name },
        "full_schema" => new { tool.Name, tool.Description, Schema = tool.ParameterSchema },
        _ => new { tool.Name, tool.Description }
    };

    #endregion
}
