using System.Text.Json;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Execution.Tests.Mocks;

/// <summary>
/// Mock provider that returns pre-configured responses per round.
/// </summary>
internal sealed class MockProvider : IAiProvider
{
    readonly Queue<AiResponse> _responses = new();

    public string ProviderName => "mock";
    public string ModelId => "mock-model";
    public TokenUsage? LastUsage => null;
    public bool IsTruncated => false;
    public string? LastRequestBody => null;
    public string? LastRawResponse => null;
    public PdfCapability PdfCapability => PdfCapability.Auto;

    public List<AiRequest> ReceivedRequests { get; } = [];

    /// <summary>
    /// Enqueue a response to return on next CompleteAsync call.
    /// </summary>
    public void EnqueueResponse(string content, IReadOnlyList<ToolCall>? toolCalls = null)
    {
        _responses.Enqueue(new AiResponse
        {
            Content = content,
            ToolCalls = toolCalls ?? [],
        });
    }

    /// <summary>
    /// Enqueue a response that includes tool calls.
    /// </summary>
    public void EnqueueToolCallResponse(string content, params ToolCall[] toolCalls)
    {
        _responses.Enqueue(new AiResponse
        {
            Content = content,
            ToolCalls = toolCalls.ToList(),
        });
    }

    public Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ReceivedRequests.Add(request);

        if (_responses.Count == 0)
            throw new InvalidOperationException("MockProvider: no more enqueued responses.");

        return Task.FromResult(_responses.Dequeue());
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(AiRequest request, CancellationToken ct = default)
        => throw new NotSupportedException("AgentLoop uses CompleteAsync, not streaming.");

    public Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AiModel>>([]);

    public Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
        => Task.FromResult(new ConnectionInfo(true, null, null, null));

    public ThinkingSupport GetThinkingSupport(string modelId) => ThinkingSupport.NotSupported;
}
