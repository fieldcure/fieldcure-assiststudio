namespace FluentView.AI.Models;

public enum AttachmentType
{
    Image,
    TextFile
}

public partial class ChatAttachment
{
    public ChatAttachment() { }

    public ChatAttachment(string fileName, AttachmentType type, byte[] data, string? mimeType = null)
    {
        FileName = fileName;
        Type = type;
        Data = data;
        MimeType = mimeType;
    }

    public string FileName { get; set; } = "";
    public AttachmentType Type { get; set; }
    public byte[] Data { get; set; } = [];
    public string? MimeType { get; set; }
}
