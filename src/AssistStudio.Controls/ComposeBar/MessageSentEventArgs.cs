using FieldCure.Ai.Providers.Models;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Provides data for the <see cref="ComposeBar.MessageSent"/> event.
/// </summary>
public partial class MessageSentEventArgs : EventArgs
{
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageSentEventArgs"/> class.
    /// </summary>
    /// <param name="text">The message text entered by the user.</param>
    /// <param name="attachments">The attachments included with the message.</param>
    public MessageSentEventArgs(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        Text = text;
        Attachments = attachments;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets the message text entered by the user.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the list of attachments included with the message.
    /// </summary>
    public IReadOnlyList<ChatAttachment> Attachments { get; }

    #endregion
}
