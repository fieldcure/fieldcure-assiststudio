using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using OoxmlMath = DocumentFormat.OpenXml.Math;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Extracts plain text from DOCX files using Open XML SDK.
/// Paragraphs are extracted as plain text with math equations converted to LaTeX notation.
/// Tables are converted to markdown format.
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
                var text = ExtractParagraphText(paragraph);
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
    /// Extracts text from a paragraph, converting inline math elements to LaTeX notation.
    /// Block-level math (oMathPara) is output on its own line.
    /// </summary>
    private static string ExtractParagraphText(Paragraph paragraph)
    {
        // Check if this paragraph contains any math elements
        var hasMath = paragraph.Descendants<OoxmlMath.OfficeMath>().Any()
                   || paragraph.Descendants<OoxmlMath.Paragraph>().Any();

        if (!hasMath)
            return paragraph.InnerText;

        // Process children sequentially to preserve math structure
        var sb = new StringBuilder();
        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                // Block math (standalone equation): m:oMathPara
                case OoxmlMath.Paragraph mathPara:
                    foreach (var oMath in mathPara.Elements<OoxmlMath.OfficeMath>())
                    {
                        var latex = OoxmlMathConverter.ToLaTeX(oMath);
                        if (!string.IsNullOrWhiteSpace(latex))
                            sb.Append($"[math: {latex}]");
                    }
                    break;

                // Inline math: m:oMath
                case OoxmlMath.OfficeMath oMath:
                    var inlineLatex = OoxmlMathConverter.ToLaTeX(oMath);
                    if (!string.IsNullOrWhiteSpace(inlineLatex))
                        sb.Append($"[math: {inlineLatex}]");
                    break;

                // Regular text run
                case Run run:
                    sb.Append(run.InnerText);
                    break;

                // Other elements (bookmarks, etc.) — extract inner text
                default:
                    var text = child.InnerText;
                    if (!string.IsNullOrEmpty(text))
                        sb.Append(text);
                    break;
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
                        .Select(ExtractParagraphText)
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
