namespace FluentView.AI.Models;

public record LocalModel(
    string Id,
    string? DisplayName,
    string? OwnedBy
) : AiModel(Id, DisplayName, OwnedBy)
{
    public long SizeBytes { get; init; }
    public string? Family { get; init; }
    public string? ParameterSize { get; init; }
    public string? QuantizationLevel { get; init; }
    public DateTime? ModifiedAt { get; init; }
    public bool IsDownloaded { get; init; }
}
