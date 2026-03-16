using System.Text;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Provides methods for processing document attachments, such as extracting text from PDF files
/// when the provider does not support native PDF input.
/// </summary>
public static class AttachmentProcessor
{
    /// <summary>
    /// Extracts plain text content from a PDF file byte array using PdfPig.
    /// </summary>
    public static string ExtractTextFromPdf(byte[] data)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(data);
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.Append(page.Text);
        }
        return sb.ToString();
    }
}
