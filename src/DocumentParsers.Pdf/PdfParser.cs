using System.Text;

namespace FieldCure.DocumentParsers.Pdf;

/// <summary>
/// Extracts text and page images from PDF files.
/// Text extraction via PdfPig, page rendering via PDFtoImage (PDFium).
/// </summary>
public sealed class PdfParser : IMediaDocumentParser
{
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions => [".pdf"];

    /// <summary>
    /// Extracts text from all pages using PdfPig.
    /// Pages are separated by page headers ("## Page {n}").
    /// </summary>
    public string ExtractText(byte[] data)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(data);
        var sb = new StringBuilder();
        var pageNumber = 0;

        foreach (var page in document.GetPages())
        {
            pageNumber++;
            if (sb.Length > 0) { sb.AppendLine(); sb.AppendLine(); }
            sb.AppendLine($"## Page {pageNumber}");
            sb.AppendLine();
            sb.Append(page.Text);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Renders each PDF page as a PNG image.
    /// </summary>
    /// <param name="data">Raw PDF bytes.</param>
    /// <param name="dpi">Render resolution. 150 balances quality vs size.</param>
    /// <returns>One PNG per page with "Page {n}" labels.</returns>
    public IReadOnlyList<DocumentImage> ExtractImages(byte[] data, int dpi = 150)
    {
        var images = new List<DocumentImage>();
        var pageCount = PDFtoImage.Conversion.GetPageCount(data);
        var options = new PDFtoImage.RenderOptions(Dpi: dpi);

        for (var i = 0; i < pageCount; i++)
        {
            using var ms = new MemoryStream();
            PDFtoImage.Conversion.SavePng(ms, data, i, options: options);
            images.Add(new DocumentImage(ms.ToArray(), $"Page {i + 1}", i));
        }

        return images;
    }
}
