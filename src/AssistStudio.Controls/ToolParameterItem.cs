namespace FieldCure.AssistStudio.Controls;

/// <summary>Represents a single parsed tool argument for display.</summary>
public sealed class ToolParameterItem
{
    /// <summary>Parameter name (e.g. "path", "content").</summary>
    public string Name { get; init; } = "";

    /// <summary>Full display value (unquoted string, pretty-printed JSON for objects/arrays).</summary>
    public string Display { get; init; } = "";

    /// <summary>Collapsed preview for long values — first N chars with newlines replaced. Empty for short values.</summary>
    public string Preview { get; init; } = "";

    /// <summary>Whether this parameter value is long enough to warrant collapsing.</summary>
    public bool IsLong { get; init; }

    /// <summary>Returns <see cref="Preview"/> for long values, <see cref="Display"/> for short.</summary>
    public string DisplayOrPreview => IsLong ? Preview : Display;
}
