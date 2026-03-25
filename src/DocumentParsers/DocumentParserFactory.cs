using System.Collections.Concurrent;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Resolves the appropriate <see cref="IDocumentParser"/> for a given file extension.
/// Thread-safe registry of all available parsers.
/// Built-in parsers (DOCX, HWPX, XLSX, PPTX) are registered automatically.
/// External parsers (e.g., PDF) can be added via <see cref="Register"/>.
/// </summary>
public static class DocumentParserFactory
{
    private static readonly ConcurrentDictionary<string, IDocumentParser> Parsers = new(StringComparer.OrdinalIgnoreCase);

    static DocumentParserFactory()
    {
        var builtIn = new IDocumentParser[]
        {
            new DocxParser(),
            new HwpxParser(),
            new XlsxParser(),
            new PptxParser(),
        };

        foreach (var p in builtIn)
            foreach (var ext in p.SupportedExtensions)
                Parsers[ext] = p;
    }

    /// <summary>
    /// Registers an external parser. Each supported extension is mapped to the parser,
    /// overwriting any previous mapping for that extension.
    /// </summary>
    /// <param name="parser">The parser to register.</param>
    public static void Register(IDocumentParser parser)
    {
        foreach (var ext in parser.SupportedExtensions)
            Parsers[ext] = parser;
    }

    /// <summary>
    /// Returns a parser for the given file extension, or null if unsupported.
    /// </summary>
    /// <param name="extension">File extension including leading dot (e.g., ".docx").</param>
    /// <returns>The parser, or <c>null</c> if the extension is not supported.</returns>
    public static IDocumentParser? GetParser(string extension)
        => Parsers.GetValueOrDefault(extension);

    /// <summary>
    /// Gets all file extensions supported by registered parsers.
    /// </summary>
    public static IEnumerable<string> SupportedExtensions => Parsers.Keys;
}
