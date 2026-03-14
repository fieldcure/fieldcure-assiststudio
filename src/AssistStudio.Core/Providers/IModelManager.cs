using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// Defines the contract for managing local AI models, including listing, downloading, and deleting.
/// </summary>
public interface IModelManager
{
    /// <summary>Lists all models currently downloaded on the local machine.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of locally available models.</returns>
    Task<IReadOnlyList<LocalModel>> ListLocalModelsAsync(CancellationToken ct = default);

    /// <summary>Searches for models available to download, optionally filtered by a query string.</summary>
    /// <param name="query">An optional search query to filter models by name.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A list of models matching the query.</returns>
    Task<IReadOnlyList<LocalModel>> SearchAvailableModelsAsync(
        string? query = null, CancellationToken ct = default);

    /// <summary>Downloads a model by name, reporting progress as it streams.</summary>
    /// <param name="modelName">The name of the model to download.</param>
    /// <param name="progress">An optional progress reporter for download status updates.</param>
    /// <param name="ct">A cancellation token.</param>
    Task DownloadModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Deletes a locally downloaded model.</summary>
    /// <param name="modelName">The name of the model to delete.</param>
    /// <param name="ct">A cancellation token.</param>
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);
}

/// <summary>
/// Reports the progress of a model download operation.
/// </summary>
/// <param name="Status">A status message describing the current download phase.</param>
/// <param name="Percent">The download completion percentage (0.0 to 1.0).</param>
/// <param name="TotalBytes">The total size of the download in bytes, if known.</param>
/// <param name="CompletedBytes">The number of bytes downloaded so far, if known.</param>
public partial record ModelDownloadProgress(
    string Status,
    double Percent,
    long? TotalBytes,
    long? CompletedBytes
);
