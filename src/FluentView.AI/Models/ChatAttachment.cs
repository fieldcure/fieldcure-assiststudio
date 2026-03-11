namespace FluentView.AI.Models;

public enum AttachmentType
{
    Image,
    TextFile
}

public class ChatAttachment
{
    public required string FileName { get; init; }
    public required AttachmentType Type { get; init; }
    public required byte[] Data { get; init; }
    public string? MimeType { get; init; }
}
