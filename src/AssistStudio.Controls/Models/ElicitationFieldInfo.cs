namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Describes a single field to render in the <see cref="ToolElicitationPanel"/>.
/// Converted from <c>ElicitRequestParams.PrimitiveSchemaDefinition</c> by the host.
/// </summary>
public sealed class ElicitationFieldInfo
{
    /// <summary>Property key returned in the elicitation result content.</summary>
    public required string Name { get; init; }

    /// <summary>Determines which UI control to render.</summary>
    public required ElicitationFieldType Type { get; init; }

    /// <summary>Optional display label. Falls back to <see cref="Name"/> if null.</summary>
    public string? Title { get; init; }

    /// <summary>Optional description or placeholder text.</summary>
    public string? Description { get; init; }

    /// <summary>Pre-selected or pre-filled default value.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Available options for <see cref="ElicitationFieldType.Enum"/> and
    /// <see cref="ElicitationFieldType.Boolean"/> fields.</summary>
    public IReadOnlyList<ElicitationOptionInfo>? Options { get; init; }
}

/// <summary>
/// The type of input control to render for an elicitation field.
/// </summary>
public enum ElicitationFieldType
{
    /// <summary>Free-text input (TextBox).</summary>
    String,

    /// <summary>Yes/No selection rendered as two option buttons.</summary>
    Boolean,

    /// <summary>Single-select from a list of options rendered as clickable buttons.</summary>
    Enum
}

/// <summary>
/// A single selectable option within an enum or boolean elicitation field.
/// </summary>
public sealed class ElicitationOptionInfo
{
    /// <summary>The constant value sent back in the elicitation result.</summary>
    public required string Value { get; init; }

    /// <summary>The text displayed to the user.</summary>
    public required string DisplayTitle { get; init; }
}
