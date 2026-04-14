namespace FieldCure.Ai.Providers.Export;

/// <summary>
/// Result of exporting a conversation to Markdown.
/// Contains the document text and any referenced media as in-memory blobs.
/// The caller is responsible for persisting these to disk or clipboard.
/// </summary>
public sealed class MarkdownExportResult
{
    /// <summary>
    /// The Markdown document text. Media references use relative paths
    /// (e.g. "media/img_001.png") that match keys in the <see cref="Media"/> dictionary.
    /// </summary>
    public required string Markdown { get; init; }

    /// <summary>
    /// Media blobs referenced by the Markdown, keyed by relative path.
    /// Caller writes these alongside the .md file at matching paths.
    /// Empty when no media is present.
    /// </summary>
    public IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Media { get; init; }
        = new Dictionary<string, ReadOnlyMemory<byte>>();
}
