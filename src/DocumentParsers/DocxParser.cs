using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Extracts plain text from DOCX files using Open XML SDK.
/// Paragraphs are extracted as plain text, and tables are converted to markdown format.
/// </summary>
public sealed class DocxParser : IDocumentParser
{
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions => [".docx"];

    /// <inheritdoc />
    public string ExtractText(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return "";

        var sb = new StringBuilder();

        foreach (var element in body.ChildElements)
        {
            if (element is Paragraph paragraph)
            {
                var text = paragraph.InnerText;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(text);
                }
            }
            else if (element is Table table)
            {
                var tableText = ConvertTableToMarkdown(table);
                if (!string.IsNullOrEmpty(tableText))
                {
                    if (sb.Length > 0) { sb.AppendLine(); sb.AppendLine(); }
                    sb.Append(tableText);
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts an OpenXml <see cref="Table"/> element to a markdown-formatted table string.
    /// The first row is treated as the header row.
    /// </summary>
    private static string ConvertTableToMarkdown(Table table)
    {
        var rows = table.Elements<TableRow>().ToList();
        if (rows.Count == 0) return "";

        var tableData = new List<string[]>();
        var maxCols = 0;

        foreach (var row in rows)
        {
            var cells = row.Elements<TableCell>()
                .Select(cell =>
                {
                    // A cell may contain multiple paragraphs — join with space
                    var cellParagraphs = cell.Elements<Paragraph>()
                        .Select(p => p.InnerText)
                        .Where(t => !string.IsNullOrEmpty(t));
                    return string.Join(" ", cellParagraphs);
                })
                .ToArray();

            if (cells.Length > maxCols) maxCols = cells.Length;
            tableData.Add(cells);
        }

        if (maxCols == 0) return "";

        var sb = new StringBuilder();
        for (var i = 0; i < tableData.Count; i++)
        {
            var row = tableData[i];
            sb.Append('|');
            for (var j = 0; j < maxCols; j++)
            {
                var cellText = j < row.Length ? row[j].Replace("|", "\\|") : "";
                sb.Append($" {cellText} |");
            }
            sb.AppendLine();

            // Separator after first row (treated as header)
            if (i == 0)
            {
                sb.Append('|');
                for (var j = 0; j < maxCols; j++)
                    sb.Append(" --- |");
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }
}
