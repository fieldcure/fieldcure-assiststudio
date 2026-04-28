using System.Text.Json.Serialization;

namespace FieldCure.Ai.Providers.Models;

/// <summary>
/// Origin of a chat attachment, used for display differentiation.
/// </summary>
public enum AttachmentSource
{
    /// <summary>File added via picker or drag-and-drop.</summary>
    File,

    /// <summary>Image pasted from clipboard or dropped.</summary>
    Image,

    /// <summary>Plain text pasted from clipboard, auto-converted to attachment.</summary>
    Pasted
}

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
    Document,

    /// <summary>An audio attachment sent as raw bytes to audio-native providers.</summary>
    Audio
}

/// <summary>
/// Specifies how a provider handles audio attachments.
/// </summary>
public enum AudioCapability
{
    /// <summary>Provider cannot accept audio input. Attachments are dropped at send time.</summary>
    NotSupported,

    /// <summary>Provider accepts raw audio bytes (e.g., Gemini 1.5+, gpt-4o-audio).</summary>
    NativeAudio
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

    /// <summary>The origin of this attachment (file picker, clipboard image, pasted text).</summary>
    [JsonIgnore]
    public AttachmentSource Source { get; set; } = AttachmentSource.File;

    /// <summary>Cached character count for display (pasted text attachments).</summary>
    [JsonIgnore]
    public int CharCount { get; set; }

    /// <summary>Cached line count for display (pasted text attachments).</summary>
    [JsonIgnore]
    public int LineCount { get; set; }

    /// <summary>
    /// Full path of the source file when attached via file picker or drag-drop.
    /// <c>null</c> for clipboard-pasted content. Used for duplicate detection.
    /// </summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    /// <summary>
    /// Indicates the image format is unsupported and cannot be sent to the API.
    /// Chip is shown with strikethrough; excluded from send payload.
    /// </summary>
    [JsonIgnore]
    public bool IsUnsupported { get; set; }

    /// <summary>
    /// Playback duration for audio attachments. Computed asynchronously after attach;
    /// <c>null</c> while pending or when the codec cannot be parsed.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? Duration { get; set; }

    #endregion
}
