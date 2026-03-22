namespace FieldCure.DocumentParsers;

/// <summary>
/// Resolves the appropriate <see cref="IDocumentParser"/> for a given file extension.
/// Thread-safe singleton registry of all available parsers.
/// </summary>
public static class DocumentParserFactory
{
    private static readonly IReadOnlyDictionary<string, IDocumentParser> Parsers;

    static DocumentParserFactory()
    {
        var list = new IDocumentParser[]
        {
            new DocxParser(),
            new HwpxParser(),
        };

        var dict = new Dictionary<string, IDocumentParser>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in list)
            foreach (var ext in p.SupportedExtensions)
                dict[ext] = p;
        Parsers = dict;
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
