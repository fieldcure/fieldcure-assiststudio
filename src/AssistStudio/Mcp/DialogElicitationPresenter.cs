using AssistStudio.Controls.Dialogs;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ModelContextProtocol.Protocol;

namespace AssistStudio.Mcp;

/// <summary>
/// Presents elicitations as modal themed content dialogs.
/// Used during Settings-driven MCP tool calls such as Outbox add_channel.
/// </summary>
internal sealed class DialogElicitationPresenter : IElicitationPresenter
{
    private readonly XamlRoot _xamlRoot;
    private readonly DispatcherQueue _dispatcherQueue;

    /// <summary>Initializes a presenter bound to the given XAML root and UI dispatcher.</summary>
    public DialogElicitationPresenter(XamlRoot xamlRoot, DispatcherQueue dispatcherQueue)
    {
        _xamlRoot = xamlRoot;
        _dispatcherQueue = dispatcherQueue;
    }

    /// <inheritdoc />
    public async Task<ElicitResult> PresentAsync(
        ElicitationRequest request,
        CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ElicitResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            CancellationTokenRegistration registration = default;
            ElicitationDialog? dialog = null;

            try
            {
                dialog = new ElicitationDialog(request)
                {
                    XamlRoot = _xamlRoot,
                };

                if (cancellationToken.CanBeCanceled)
                {
                    registration = cancellationToken.Register(() =>
                        _dispatcherQueue.TryEnqueue(() => dialog.Hide()));
                }

                var result = await dialog.ShowAsync();
                tcs.TrySetResult(result == ContentDialogResult.Primary
                    ? dialog.CreateAcceptResult()
                    : new ElicitResult { Action = "cancel" });
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
            finally
            {
                registration.Dispose();
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
