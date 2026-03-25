namespace FieldCure.DocumentParsers.Pdf;

/// <summary>
/// Registers PDF parser with <see cref="DocumentParserFactory"/>.
/// </summary>
public static class DocumentParserFactoryExtensions
{
    /// <summary>
    /// Registers PDF parser (PdfPig + PDFtoImage) with the factory.
    /// Call once at startup.
    /// </summary>
    public static void AddPdfSupport()
        => DocumentParserFactory.Register(new PdfParser());
}
