using FieldCure.DocumentParsers;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Provides methods for processing document attachments, such as extracting text from PDF files
/// when the provider does not support native PDF input.
/// </summary>
public static class AttachmentProcessor
{
    /// <summary>
    /// Extracts plain text content from a PDF file byte array.
    /// Requires PDF parser to be registered via <see cref="DocumentParserFactory.Register"/> at startup.
    /// </summary>
    public static string ExtractTextFromPdf(byte[] data)
        => DocumentParserFactory.GetParser(".pdf")!.ExtractText(data);

    /// <summary>
    /// Renders each page of a PDF document to a PNG image byte array.
    /// Requires PDF parser (implementing <see cref="IMediaDocumentParser"/>) to be registered at startup.
    /// </summary>
    /// <param name="data">The raw PDF file bytes.</param>
    /// <param name="dpi">Render resolution in dots per inch. Default is 150.</param>
    /// <returns>A list of PNG byte arrays, one per page.</returns>
    public static IReadOnlyList<byte[]> RenderPdfPages(byte[] data, int dpi = 150)
        => ((IMediaDocumentParser)DocumentParserFactory.GetParser(".pdf")!).ExtractImages(data, dpi)
            .Select(img => img.Data).ToList();
}
