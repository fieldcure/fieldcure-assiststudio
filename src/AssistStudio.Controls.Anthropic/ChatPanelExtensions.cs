using System.Text.Json;
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
    /// <param name="modelId">Model identifier (e.g., "claude-sonnet-4-6").</param>
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
    /// Builds a ready-to-send <see cref="MessageCreateParams"/> from the current conversation,
    /// with Anthropic prompt caching enabled by default. Consumers who invoke this helper
    /// automatically benefit from cache hits on repeated prefixes (system prompt, attachments,
    /// tool results). To opt out of caching, assign <c>CacheControl = null</c> to the returned
    /// params before sending.
    /// </summary>
    /// <param name="panel">The chat panel.</param>
    /// <param name="model">Model identifier (e.g., "claude-sonnet-4-6").</param>
    /// <param name="maxTokens">Maximum output tokens.</param>
    /// <param name="tools">
    /// Optional list of tools to expose to the model. Each tool's
    /// <see cref="IAssistTool.ParameterSchema"/> must be a JSON Schema object describing
    /// its <c>input</c> shape; it is forwarded as-is to <see cref="Tool.InputSchema"/>.
    /// Pass null or an empty list when the turn does not need tool calling.
    /// </param>
    /// <returns>Populated <see cref="MessageCreateParams"/> suitable for <c>client.Messages.CreateAsync</c> or streaming.</returns>
    public static MessageCreateParams BuildAnthropicParams(
        this ChatPanel panel, string model, long maxTokens,
        IList<IAssistTool>? tools = null)
    {
        var conv = panel.GetConversationAsAnthropicMessages();

        List<ToolUnion>? toolList = null;
        if (tools is { Count: > 0 })
        {
            toolList = new List<ToolUnion>(tools.Count);
            foreach (var t in tools)
            {
                toolList.Add(new Tool
                {
                    Name = t.Name,
                    Description = t.Description,
                    InputSchema = ParseInputSchema(t.ParameterSchema),
                });
            }
        }

        return new MessageCreateParams
        {
            Model = model,
            MaxTokens = maxTokens,
            Messages = conv.Messages,
            System = conv.SystemPrompt is null ? null : new MessageCreateParamsSystem(conv.SystemPrompt),
            Tools = toolList,

            // v1.0 attachment caching — top-level marker enables Anthropic automatic
            // prompt caching. API places the breakpoint at the last cacheable block
            // and advances it as the conversation grows. See:
            // https://docs.claude.com/en/docs/build-with-claude/prompt-caching
            CacheControl = new CacheControlEphemeral { Ttl = Ttl.Ttl5m },
        };
    }

    /// <summary>
    /// Parses an <see cref="IAssistTool.ParameterSchema"/> JSON string into an Anthropic SDK
    /// <see cref="InputSchema"/>. The schema is expected to be a JSON object whose top-level
    /// keys (typically <c>type</c>, <c>properties</c>, <c>required</c>) are forwarded as raw
    /// JSON elements.
    /// </summary>
    private static InputSchema ParseInputSchema(string schemaJson)
    {
        using var doc = JsonDocument.Parse(schemaJson);
        var dict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.Clone();
        return InputSchema.FromRawUnchecked(dict);
    }

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
