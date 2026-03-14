namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Represents a locally available AI model with additional metadata such as size and quantization details.
/// </summary>
/// <param name="Id">The unique model identifier.</param>
/// <param name="DisplayName">A human-readable display name for the model.</param>
/// <param name="OwnedBy">The organization or entity that owns the model.</param>
public partial record LocalModel(
    string Id,
    string? DisplayName,
    string? OwnedBy
) : AiModel(Id, DisplayName, OwnedBy)
{
    /// <summary>The model file size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>The model family (e.g., "llama", "gemma").</summary>
    public string? Family { get; init; }

    /// <summary>The parameter size description (e.g., "7B", "70B").</summary>
    public string? ParameterSize { get; init; }

    /// <summary>The quantization level (e.g., "Q4_0", "Q8_0").</summary>
    public string? QuantizationLevel { get; init; }

    /// <summary>The date and time the model was last modified locally.</summary>
    public DateTime? ModifiedAt { get; init; }

    /// <summary>Whether the model has been downloaded to the local machine.</summary>
    public bool IsDownloaded { get; init; }
}
