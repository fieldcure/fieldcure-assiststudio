using FluentView.AI.Models;

namespace FluentView.AI.Controls;

public class MessageSentEventArgs : EventArgs
{
    public MessageSentEventArgs(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        Text = text;
        Attachments = attachments;
    }

    public string Text { get; }
    public IReadOnlyList<ChatAttachment> Attachments { get; }
}
