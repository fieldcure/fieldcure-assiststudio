using FluentView.AI.Models;

namespace FluentView.AI.Providers;

public interface IModelManager
{
    Task<IReadOnlyList<LocalModel>> ListLocalModelsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LocalModel>> SearchAvailableModelsAsync(
        string? query = null, CancellationToken ct = default);
    Task DownloadModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
}

public partial record ModelDownloadProgress(
    string Status,
    double Percent,
    long? TotalBytes,
    long? CompletedBytes
);
