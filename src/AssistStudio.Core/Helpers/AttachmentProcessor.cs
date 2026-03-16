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

    /// <summary>
    /// Renders each page of a PDF document to a PNG image byte array.
    /// </summary>
    /// <param name="data">The raw PDF file bytes.</param>
    /// <param name="dpi">Render resolution in dots per inch. Default is 150.</param>
    /// <returns>A list of PNG byte arrays, one per page.</returns>
    public static IReadOnlyList<byte[]> RenderPdfPages(byte[] data, int dpi = 150)
    {
        var pages = new List<byte[]>();
        var pageCount = PDFtoImage.Conversion.GetPageCount(data);
        var options = new PDFtoImage.RenderOptions(Dpi: dpi);
        for (var i = 0; i < pageCount; i++)
        {
            using var ms = new MemoryStream();
            PDFtoImage.Conversion.SavePng(ms, data, i, options: options);
            pages.Add(ms.ToArray());
        }
        return pages;
    }
}
