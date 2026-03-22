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
