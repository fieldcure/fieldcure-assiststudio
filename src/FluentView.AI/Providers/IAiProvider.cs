using FluentView.AI.Models;

namespace FluentView.AI.Providers;

public interface IAiProvider
{
    string ProviderName { get; }
    string ModelId { get; }
    Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default);
    IAsyncEnumerable<string> StreamAsync(AiRequest request, CancellationToken ct = default);
    TokenUsage? LastUsage { get; }
    bool IsTruncated { get; }
    Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default);
    Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default);
}
