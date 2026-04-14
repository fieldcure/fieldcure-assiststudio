using Anthropic.Models.Messages;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Anthropic;

namespace FieldCure.AssistStudio.Controls.Anthropic;

/// <summary>
/// Extension methods for integrating <see cref="ChatPanel"/> with the Anthropic SDK.
/// </summary>
public static class ChatPanelExtensions
{
    /// <summary>
    /// Begins an assistant turn configured for Anthropic SDK streaming.
    /// The returned handle should be used with <see cref="StreamAnthropicAsync"/> and disposed when done.
    /// </summary>
    /// <param name="panel">The chat panel.</param>
    /// <param name="providerName">Display name (e.g., "Claude").</param>
    /// <param name="modelId">Model identifier (e.g., "claude-sonnet-4-20250514").</param>
    /// <returns>An <see cref="AssistantTurnHandle"/> for the new turn.</returns>
    public static AssistantTurnHandle BeginAnthropicTurn(
        this ChatPanel panel, string? providerName = null, string? modelId = null)
        => panel.BeginAssistantTurn(providerName, modelId);

    /// <summary>
    /// Converts the current conversation in the <see cref="ChatPanel"/> to Anthropic SDK message format.
    /// </summary>
    /// <param name="panel">The chat panel.</param>
    /// <returns>A <see cref="ConversionResult"/> containing messages and system prompt.</returns>
    public static ConversionResult GetConversationAsAnthropicMessages(this ChatPanel panel)
        => AnthropicMessageConverter.Convert(panel.GetConversationSnapshot());

    /// <summary>
    /// Streams an Anthropic SDK raw event stream into the assistant turn,
    /// mapping SDK events to Controls <see cref="StreamEvent"/> instances.
    /// A new <see cref="AnthropicStreamEventMapper"/> is created internally per call.
    /// </summary>
    /// <param name="handle">The assistant turn handle.</param>
    /// <param name="sdkStream">The raw Anthropic SDK event stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated stream result.</returns>
    public static Task<StreamResult> StreamAnthropicAsync(
        this AssistantTurnHandle handle,
        IAsyncEnumerable<RawMessageStreamEvent> sdkStream,
        CancellationToken ct = default)
    {
        var mapper = new AnthropicStreamEventMapper();
        return handle.ConsumeStreamAsync(mapper.MapAsync(sdkStream, ct), ct);
    }
}
