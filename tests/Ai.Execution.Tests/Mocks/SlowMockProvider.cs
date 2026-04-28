using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution.Tests.Mocks;

/// <summary>
/// Mock AI provider that delays responses so execution tests can cover cancellation and timing behavior.
/// </summary>
internal sealed class SlowMockProvider : IAiProvider
{
    readonly TimeSpan _delay;

    public SlowMockProvider(TimeSpan delay) => _delay = delay;

    public string ProviderName => "slow-mock";
    public string ModelId => "slow-model";
    public TokenUsage? LastUsage => null;
    public bool IsTruncated => false;
    public string? LastRequestBody => null;
    public string? LastRawResponse => null;
    public PdfCapability PdfCapability => PdfCapability.Auto;
    public AudioCapability AudioCapability => AudioCapability.NotSupported;
    public ToolCallingSupport ToolCallingSupport => ToolCallingSupport.Supported;

    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        await Task.Delay(_delay, ct); // Will throw OperationCanceledException on timeout
        return new AiResponse { Content = "Should not reach here" };
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(AiRequest request, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModel>>([]);

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new ConnectionInfo(true, null, null, null));

    public ThinkingSupport GetThinkingSupport(string modelId) => ThinkingSupport.NotSupported;
}
