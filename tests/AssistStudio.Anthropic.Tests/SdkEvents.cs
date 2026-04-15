using Anthropic.Models.Messages;

namespace FieldCure.AssistStudio.Anthropic.Tests;

/// <summary>
/// Factory methods for constructing Anthropic SDK streaming event types in tests.
/// </summary>
internal static class SdkEvents
{
    public static RawMessageStreamEvent TextBlockStart(long index) =>
        new RawContentBlockStartEvent
        {
            ContentBlock = new TextBlock { Text = "", Citations = null },
            Index = index,
        };

    public static RawMessageStreamEvent ThinkingBlockStart(long index) =>
        new RawContentBlockStartEvent
        {
            ContentBlock = new ThinkingBlock { Thinking = "", Signature = "" },
            Index = index,
        };

    public static RawMessageStreamEvent RedactedThinkingBlockStart(long index) =>
        new RawContentBlockStartEvent
        {
            ContentBlock = new RedactedThinkingBlock { Data = "" },
            Index = index,
        };

    public static RawMessageStreamEvent TextDeltaEvent(long index, string text) =>
        new RawContentBlockDeltaEvent
        {
            Index = index,
            Delta = new TextDelta(text),
        };

    public static RawMessageStreamEvent ThinkingDeltaEvent(long index, string thinking) =>
        new RawContentBlockDeltaEvent
        {
            Index = index,
            Delta = new ThinkingDelta(thinking),
        };

    public static RawMessageStreamEvent CitationsDeltaEvent(long index) =>
        new RawContentBlockDeltaEvent
        {
            Index = index,
            Delta = new CitationsDelta
            {
                Citation = new CitationCharLocation
                {
                    CitedText = "cited",
                    DocumentIndex = 0,
                    DocumentTitle = "doc",
                    EndCharIndex = 5,
                    FileID = "file",
                    StartCharIndex = 0,
                },
            },
        };

    public static RawMessageStreamEvent SignatureDeltaEvent(long index) =>
        new RawContentBlockDeltaEvent
        {
            Index = index,
            Delta = new SignatureDelta("sig"),
        };

    public static RawMessageStreamEvent BlockStop(long index) =>
        new RawContentBlockStopEvent { Index = index };

    public static RawMessageStreamEvent MessageStart(long inputTokens = 0, long outputTokens = 0) =>
        new RawMessageStartEvent(new Message
        {
            ID = "msg_test",
            Content = [],
            Model = "claude-sonnet-4-6",
            StopDetails = null,
            StopReason = StopReason.EndTurn,
            StopSequence = null,
            Container = null,
            Usage = new Usage
            {
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CacheCreation = null,
                CacheCreationInputTokens = null,
                CacheReadInputTokens = null,
                ServerToolUse = null,
                ServiceTier = UsageServiceTier.Standard,
                InferenceGeo = null,
            },
        });

    public static RawMessageStreamEvent MessageDelta(long outputTokens, StopReason stopReason = StopReason.EndTurn) =>
        new RawMessageDeltaEvent
        {
            Delta = new Delta
            {
                Container = null,
                StopDetails = null,
                StopReason = stopReason,
                StopSequence = null,
            },
            Usage = new MessageDeltaUsage
            {
                OutputTokens = outputTokens,
                CacheCreationInputTokens = null,
                CacheReadInputTokens = null,
                InputTokens = null,
                ServerToolUse = null,
            },
        };

    public static RawMessageStreamEvent MessageStop() => new RawMessageStopEvent();

    /// <summary>
    /// Converts a sequence of RawMessageStreamEvent to IAsyncEnumerable.
    /// </summary>
    public static async IAsyncEnumerable<RawMessageStreamEvent> ToAsyncEnumerable(
        params RawMessageStreamEvent[] events)
    {
        foreach (var e in events)
            yield return e;
        await Task.CompletedTask;
    }
}
