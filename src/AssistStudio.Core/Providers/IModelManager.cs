using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// Defines the contract for managing local AI models, including listing, downloading, and deleting.
/// </summary>
public interface IModelManager
{
    #region Methods

    /// <summary>Lists all models currently downloaded on the local machine.</summary>
    Task<IReadOnlyList<LocalModel>> ListLocalModelsAsync(CancellationToken ct = default);

    /// <summary>Searches for models available to download, optionally filtered by a query string.</summary>
    Task<IReadOnlyList<LocalModel>> SearchAvailableModelsAsync(
        string? query = null, CancellationToken ct = default);

    /// <summary>Downloads a model by name, reporting progress as it streams.</summary>
    Task DownloadModelAsync(
        string modelName,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Deletes a locally downloaded model.</summary>
    Task DeleteModelAsync(string modelName, CancellationToken ct = default);

    #endregion
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
