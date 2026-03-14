namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Specifies the type of file attached to a chat message.
/// </summary>
public enum AttachmentType
{
    /// <summary>An image attachment sent as inline vision data.</summary>
    Image,

    /// <summary>A plain text file whose contents are appended to the message.</summary>
    TextFile,

    /// <summary>A document attachment for future multimodal support (e.g., PDF Vision).</summary>
    Document
}

/// <summary>
/// Represents a file attachment on a chat message, such as an image or text file.
/// </summary>
public partial class ChatAttachment
{
    /// <summary>
    /// Initializes a new empty <see cref="ChatAttachment"/>.
    /// </summary>
    public ChatAttachment() { }

    /// <summary>
    /// Initializes a new <see cref="ChatAttachment"/> with the specified file name, type, data, and optional MIME type.
    /// </summary>
    /// <param name="fileName">The name of the attached file.</param>
    /// <param name="type">The type of attachment.</param>
    /// <param name="data">The raw byte content of the file.</param>
    /// <param name="mimeType">The MIME type of the file, or <see langword="null"/> to infer automatically.</param>
    public ChatAttachment(string fileName, AttachmentType type, byte[] data, string? mimeType = null)
    {
        FileName = fileName;
        Type = type;
        Data = data;
        MimeType = mimeType;
    }

    /// <summary>The name of the attached file.</summary>
    public string FileName { get; set; } = "";

    /// <summary>The type of attachment.</summary>
    public AttachmentType Type { get; set; }

    /// <summary>The raw byte content of the file.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>The MIME type of the file (e.g., "image/png").</summary>
    public string? MimeType { get; set; }
}
