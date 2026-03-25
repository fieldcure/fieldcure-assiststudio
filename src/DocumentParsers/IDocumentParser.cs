namespace FieldCure.DocumentParsers;

/// <summary>
/// Extracts plain text from a document file for indexing and RAG consumption.
/// Implementations handle specific file formats (DOCX, HWPX, etc.).
/// </summary>
public interface IDocumentParser
{
    /// <summary>
    /// Gets the file extensions this parser handles (e.g., ".docx", ".hwpx").
    /// Extensions include the leading dot and are lowercase.
    /// </summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// Extracts plain text from document bytes.
    /// Paragraphs are separated by newlines. Tables are converted to markdown format.
    /// </summary>
    /// <param name="data">Raw bytes of the document file.</param>
    /// <returns>Extracted text suitable for LLM consumption.</returns>
    string ExtractText(byte[] data);
}

/// <summary>
/// Extends <see cref="IDocumentParser"/> with image extraction capability.
/// Used for formats that contain embedded images or renderable pages (PDF, PPTX).
/// </summary>
public interface IMediaDocumentParser : IDocumentParser
{
    /// <summary>
    /// Extracts images from the document.
    /// For PDF: each page rendered as PNG.
    /// For PPTX: embedded images or slide renders.
    /// </summary>
    /// <param name="data">Raw document bytes.</param>
    /// <param name="dpi">Render resolution for page-based formats (default: 150).</param>
    /// <returns>List of extracted images with metadata.</returns>
    IReadOnlyList<DocumentImage> ExtractImages(byte[] data, int dpi = 150);
}

/// <summary>
/// An image extracted from a document.
/// </summary>
/// <param name="Data">PNG image bytes.</param>
/// <param name="Label">Human-readable label (e.g., "Page 1", "Slide 3", "Figure 2").</param>
/// <param name="Index">Zero-based position in the document.</param>
public record DocumentImage(byte[] Data, string Label, int Index);
