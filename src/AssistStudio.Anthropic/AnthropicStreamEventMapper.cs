using Anthropic.Models.Messages;
using FieldCure.Ai.Providers.Models;
using System.Runtime.CompilerServices;
// FieldCure.Ai.Providers.Models.StopReason (our domain enum) collides with the
// Anthropic SDK's per-message StopReason. Alias the SDK type so this file can
// keep reading the SDK's stop_reason field while the rest of the assembly still
// refers to the domain enum implicitly.
using SdkStopReason = Anthropic.Models.Messages.StopReason;

namespace FieldCure.AssistStudio.Anthropic;

/// <summary>
/// Maps Anthropic SDK <see cref="RawMessageStreamEvent"/> instances to Controls <see cref="StreamEvent"/> instances.
/// This is a stateful, per-stream mapper — create a new instance for each stream. Do NOT reuse across streams.
/// </summary>
public sealed class AnthropicStreamEventMapper
{
    /// <summary>Tracks content block metadata by stream index.</summary>
    private readonly Dictionary<long, BlockInfo> _blocks = new();

    /// <summary>Cumulative input token count from the message start event.</summary>
    private long _inputTokens;

    /// <summary>Final output token count from the message delta event (not incremental — overwrites on each delta).</summary>
    private long _outputTokens;

    /// <summary>Prompt-cache write tokens reported in the message start event, if any.</summary>
    private long? _cacheCreationInputTokens;

    /// <summary>Prompt-cache read tokens reported in the message start event, if any.</summary>
    private long? _cacheReadInputTokens;

    /// <summary>Guards against emitting duplicate <see cref="StreamEvent.StreamCompleted"/> events.</summary>
    private bool _streamCompleted;

    /// <summary>Identifies the kind of content block being streamed.</summary>
    private enum BlockKind { Text, Thinking, ToolUse, ServerToolUse, Unknown }

    /// <summary>Metadata for a registered content block: its kind and optional tool call ID.</summary>
    private readonly record struct BlockInfo(BlockKind Kind, string? ToolCallId);

    /// <summary>
    /// Asynchronously maps a stream of Anthropic SDK events to Controls StreamEvent instances.
    /// </summary>
    /// <param name="source">The raw Anthropic SDK event stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>An async enumerable of mapped <see cref="StreamEvent"/> instances.</returns>
    public async IAsyncEnumerable<StreamEvent> MapAsync(
        IAsyncEnumerable<RawMessageStreamEvent> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var raw in source.WithCancellation(ct))
        {
            // Hot path: content block deltas (most frequent events)
            if (raw.TryPickContentBlockDelta(out var blockDelta))
            {
                if (!_blocks.TryGetValue(blockDelta.Index, out var info))
                    continue; // Orphan delta — no matching block start

                if (blockDelta.Delta.TryPickText(out var textDelta))
                {
                    if (info.Kind == BlockKind.Text)
                        yield return new StreamEvent.TextDelta(textDelta.Text);
                }
                else if (blockDelta.Delta.TryPickThinking(out var thinkingDelta))
                {
                    if (info.Kind == BlockKind.Thinking)
                        yield return new StreamEvent.ThinkingDelta(thinkingDelta.Thinking);
                }
                else if (blockDelta.Delta.TryPickInputJson(out var inputJsonDelta))
                {
                    // Tool input streams incrementally as PartialJson chunks. Forward only
                    // when the block is a regular ToolUse (not ServerToolUse); the latter
                    // is handled internally by the SDK and not surfaced to consumers.
                    if (info.Kind == BlockKind.ToolUse && info.ToolCallId is { } toolId)
                        yield return new StreamEvent.ToolCallDelta(toolId, inputJsonDelta.PartialJson);
                }
                // CitationsDelta, SignatureDelta — silently ignored (SignatureDelta becomes
                // relevant in Phase B for tool_use + thinking signature round-trip).
                continue;
            }

            // Content block starts
            if (raw.TryPickContentBlockStart(out var blockStart))
            {
                var cb = blockStart.ContentBlock;

                if (cb.TryPickText(out _))
                {
                    _blocks[blockStart.Index] = new BlockInfo(BlockKind.Text, null);
                }
                else if (cb.TryPickThinking(out _))
                {
                    _blocks[blockStart.Index] = new BlockInfo(BlockKind.Thinking, null);
                }
                else if (cb.TryPickToolUse(out var toolUse))
                {
                    _blocks[blockStart.Index] = new BlockInfo(BlockKind.ToolUse, toolUse.ID);
                    yield return new StreamEvent.ToolCallStart(toolUse.ID, toolUse.Name);
                }
                else if (cb.TryPickServerToolUse(out var serverTool))
                {
                    // ServerToolUse is intentionally not emitted as ToolCallStart — these are
                    // Anthropic-hosted tools (web_search, code_execution) whose lifecycle is
                    // handled inside the SDK; consumers cannot intercept or execute them.
                    _blocks[blockStart.Index] = new BlockInfo(BlockKind.ServerToolUse, serverTool.ID);
                }
                else
                {
                    _blocks[blockStart.Index] = new BlockInfo(BlockKind.Unknown, null);
                }

                continue;
            }

            // Message delta — contains usage info and stop reason
            if (raw.TryPickDelta(out var msgDelta))
            {
                _outputTokens = msgDelta.Usage.OutputTokens;
                var usage = new TokenUsage(ClampToInt(_inputTokens), ClampToInt(_outputTokens))
                {
                    CacheCreationInputTokens = _cacheCreationInputTokens,
                    CacheReadInputTokens = _cacheReadInputTokens,
                };
                yield return new StreamEvent.Usage(usage);

                var isTruncated = msgDelta.Delta.StopReason is { } sr && sr.Value() == SdkStopReason.MaxTokens;
                _streamCompleted = true;
                yield return new StreamEvent.StreamCompleted(isTruncated);
                continue;
            }

            // Message start — capture initial usage
            if (raw.TryPickStart(out var msgStart))
            {
                _inputTokens = msgStart.Message.Usage.InputTokens;
                _outputTokens = msgStart.Message.Usage.OutputTokens;
                _cacheCreationInputTokens = msgStart.Message.Usage.CacheCreationInputTokens;
                _cacheReadInputTokens = msgStart.Message.Usage.CacheReadInputTokens;
                continue;
            }

            // Message stop — fallback StreamCompleted if not already emitted
            if (raw.TryPickStop(out _))
            {
                if (!_streamCompleted)
                {
                    _streamCompleted = true;
                    yield return new StreamEvent.StreamCompleted(false);
                }
                continue;
            }

            // ContentBlockStop, unknown events — silently ignored
        }
    }

    /// <summary>Clamps a <see langword="long"/> token count to <see cref="int"/> range, treating negatives as zero.</summary>
    private static int ClampToInt(long value)
        => value > int.MaxValue ? int.MaxValue
         : value < 0 ? 0
         : (int)value;
}
