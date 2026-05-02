using FieldCure.AssistStudio.Controls;
using ModelContextProtocol.Protocol;

namespace AssistStudio.Mcp;

/// <summary>
/// Presents elicitations inline in the active conversation's chat panel.
/// </summary>
internal sealed class ChatPanelElicitationPresenter : IElicitationPresenter
{
    private readonly Func<ChatPanel?> _getPanel;

    /// <summary>Initializes a presenter that resolves the active chat panel lazily.</summary>
    public ChatPanelElicitationPresenter(Func<ChatPanel?> getPanel)
    {
        _getPanel = getPanel;
    }

    /// <inheritdoc />
    public async Task<ElicitResult> PresentAsync(
        ElicitationRequest request,
        CancellationToken cancellationToken)
    {
        var panel = _getPanel();
        if (panel is null)
            return new ElicitResult { Action = "cancel" };

        var tcs = new TaskCompletionSource<ElicitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!panel.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var (action, content) = await panel.RequestElicitationAsync(
                    request.ToolName,
                    request.ServerName,
                    request.Message,
                    request.Fields);

                tcs.TrySetResult(McpElicitationMapper.ConvertToElicitResult(action, content));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }))
        {
            return new ElicitResult { Action = "cancel" };
        }

        try
        {
            return await tcs.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new ElicitResult { Action = "cancel" };
        }
    }
}
