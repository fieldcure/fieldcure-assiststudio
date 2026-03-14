namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Represents an AI model available from a provider.
/// </summary>
/// <param name="Id">The unique model identifier used in API requests.</param>
/// <param name="DisplayName">A human-readable display name for the model.</param>
/// <param name="OwnedBy">The organization or entity that owns the model.</param>
public partial record AiModel(
    string Id,
    string? DisplayName,
    string? OwnedBy
);
