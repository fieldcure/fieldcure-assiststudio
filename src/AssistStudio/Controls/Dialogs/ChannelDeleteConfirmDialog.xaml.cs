namespace AssistStudio.Controls.Dialogs;

/// <summary>
/// Confirms removal of an Outbox messaging channel.
/// </summary>
public sealed partial class ChannelDeleteConfirmDialog : ThemedContentDialog
{
    /// <summary>Gets the localized confirmation message shown by the dialog.</summary>
    public string Message { get; }

    /// <summary>Initializes a new confirmation dialog for the given channel.</summary>
    public ChannelDeleteConfirmDialog(string channelId)
    {
        Message = string.Format(
            Loader.GetString("Connect_DeleteChannelConfirmMessage")
                ?? "Delete channel \"{0}\"? This removes its stored setup data.",
            channelId);
        InitializeComponent();
    }
}
