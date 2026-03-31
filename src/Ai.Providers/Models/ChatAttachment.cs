namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Specifies the type of file attached to a chat message.
/// </summary>
public enum AttachmentType
{
    /// <summary>An image attachment sent as inline vision data.</summary>
    Image,

    /// <summary>A plain text file whose contents are appended to the message.</summary>
    TextFile,

    /// <summary>A document attachment for multimodal support (e.g., native PDF).</summary>
    Document
}

/// <summary>
/// Specifies how a provider handles PDF document attachments.
/// </summary>
public enum PdfCapability
{
    /// <summary>Automatically determined by the provider type.</summary>
    Auto,

    /// <summary>PDF is text-extracted client-side and sent as plain text.</summary>
    TextExtraction,

    /// <summary>PDF raw bytes are sent natively to the provider API.</summary>
    NativePdf,

    /// <summary>PDF pages are rendered as images and sent via vision input.</summary>
    PageAsImage
}

/// <summary>
/// Represents a file attachment on a chat message, such as an image or text file.
/// </summary>
public partial class ChatAttachment
{
    #region Constructors

    /// <summary>
    /// Initializes a new empty <see cref="ChatAttachment"/>.
    /// </summary>
    public ChatAttachment() { }

    /// <summary>
    /// Initializes a new <see cref="ChatAttachment"/> with the specified file name, type, data, and optional MIME type.
    /// </summary>
    public ChatAttachment(string fileName, AttachmentType type, byte[] data, string? mimeType = null)
    {
        FileName = fileName;
        Type = type;
        Data = data;
        MimeType = mimeType;
    }

    #endregion

    #region Properties

    /// <summary>The name of the attached file.</summary>
    public string FileName { get; set; } = "";

    /// <summary>The type of attachment.</summary>
    public AttachmentType Type { get; set; }

    /// <summary>The raw byte content of the file.</summary>
    public byte[] Data { get; set; } = [];

    /// <summary>The MIME type of the file (e.g., "image/png").</summary>
    public string? MimeType { get; set; }

    #endregion
}
