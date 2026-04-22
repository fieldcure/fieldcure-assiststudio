using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Represents an active assistant turn that consumes a stream of <see cref="StreamEvent"/> instances
/// and renders them into the <see cref="ChatPanel"/>. Disposing the handle finalizes the message
/// and restores the input area. Always use with <c>await using</c>.
/// </summary>
/// <remarks>
/// Obtain an instance via <see cref="ChatPanel.BeginAssistantTurn"/>.
/// Only one handle may be active at a time per ChatPanel.
/// The <see cref="CancellationToken"/> is wired to the ChatPanel's Stop button —
/// pass it to your SDK streaming call to enable user-initiated cancellation.
/// </remarks>
public sealed class AssistantTurnHandle : IAsyncDisposable
{
    private readonly ChatPanel _panel;
    private readonly System.Diagnostics.Stopwatch _elapsedSw = System.Diagnostics.Stopwatch.StartNew();
    private bool _disposed;

    internal AssistantTurnHandle(ChatPanel panel, ChatMessage message, CancellationToken cancellationToken)
    {
        _panel = panel;
        Message = message;
        CancellationToken = cancellationToken;
    }

    /// <summary>The assistant message being streamed into.</summary>
    public ChatMessage Message { get; }

    /// <summary>Whether this handle has been disposed.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>
    /// A cancellation token that is triggered when the user clicks the Stop button.
    /// Pass this to your SDK streaming call to enable user-initiated cancellation.
    /// </summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// Consumes a stream of events into the assistant message. Returns aggregated usage, truncation info, and tool calls.
    /// </summary>
    /// <param name="events">The stream of events to consume.</param>
    /// <param name="ct">Additional cancellation token (linked with <see cref="CancellationToken"/>).</param>
    /// <returns>Aggregated stream result.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the handle has been disposed.</exception>
    public Task<StreamResult> ConsumeStreamAsync(IAsyncEnumerable<StreamEvent> events, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Link the caller's token with the Stop button token
        var linked = ct == default ? CancellationToken
            : CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, ct).Token;

        return _panel.ConsumeStreamInternalAsync(events, Message, linked);
    }

    /// <summary>
    /// Finalizes the assistant message, clears the streaming state, and restores the input area.
    /// This method is idempotent — calling it multiple times has no effect.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _elapsedSw.Stop();
        Message.ElapsedSeconds = _elapsedSw.Elapsed.TotalSeconds;

        await _panel.FinalizeHandleAsync(Message);
    }
}
