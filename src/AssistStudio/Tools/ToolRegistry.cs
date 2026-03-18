using FieldCure.AssistStudio.Models;

namespace AssistStudio.Tools;

/// <summary>
/// Static registry that maps tool names to <see cref="IAssistTool"/> instances.
/// Tools are registered at app startup and resolved by name when a preset is activated.
/// </summary>
public static class ToolRegistry
{
    #region Fields

    /// <summary>Backing store for registered tools.</summary>
    private static readonly Dictionary<string, IAssistTool> _tools = [];

    #endregion

    #region Public Methods

    /// <summary>
    /// Registers a tool instance. Overwrites any existing tool with the same name.
    /// </summary>
    public static void Register(IAssistTool tool) => _tools[tool.Name] = tool;

    /// <summary>
    /// Resolves a list of tool names to their registered <see cref="IAssistTool"/> instances.
    /// Unknown names are silently skipped.
    /// </summary>
    public static IReadOnlyList<IAssistTool> Resolve(IEnumerable<string> names)
        => [.. names.Where(_tools.ContainsKey).Select(n => _tools[n])];

    /// <summary>
    /// Gets all registered tools.
    /// </summary>
    public static IReadOnlyList<IAssistTool> All => [.. _tools.Values];

    #endregion
}
