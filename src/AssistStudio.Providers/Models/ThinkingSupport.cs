namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Describes the level of extended thinking support for a model.
/// </summary>
public enum ThinkingSupport
{
    /// <summary>The model does not support extended thinking.</summary>
    NotSupported,

    /// <summary>The model supports thinking and the user can enable or disable it.</summary>
    Optional,

    /// <summary>The model always has thinking enabled and it cannot be disabled.</summary>
    Required
}
