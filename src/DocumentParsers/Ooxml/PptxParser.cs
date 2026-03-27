using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Extracts text from PowerPoint (.pptx) files using OpenXML SDK.
/// Each slide is output with a heading and its text frames.
/// Tables on slides are converted to markdown format.
/// Speaker notes are appended with [Notes] prefix.
/// </summary>
public sealed class PptxParser : IDocumentParser
{
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions => [".pptx"];

    /// <inheritdoc />
    public string ExtractText(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var doc = PresentationDocument.Open(stream, false);
        var presentationPart = doc.PresentationPart;
        if (presentationPart is null) return "";

        var slideIdList = presentationPart.Presentation?.SlideIdList;
        if (slideIdList is null) return "";
        var slideIds = slideIdList.Elements<SlideId>().ToList();
        if (slideIds.Count == 0) return "";

        var sb = new StringBuilder();
        var slideNumber = 0;

        foreach (var slideId in slideIds)
        {
            slideNumber++;
            var slidePart = (SlidePart)presentationPart.GetPartById(slideId.RelationshipId!);

            if (sb.Length > 0) { sb.AppendLine(); sb.AppendLine(); }
            sb.AppendLine($"## Slide {slideNumber}");
            sb.AppendLine();

            // Extract text from shape tree
            var shapeTree = slidePart.Slide?.CommonSlideData?.ShapeTree;
            if (shapeTree is not null)
                ExtractShapeTreeText(shapeTree, sb);

            // Speaker notes
            var notesPart = slidePart.NotesSlidePart;
            if (notesPart is not null)
            {
                var notesText = ExtractNotesText(notesPart);
                if (!string.IsNullOrWhiteSpace(notesText))
                {
                    sb.AppendLine();
                    sb.AppendLine($"[Notes] {notesText}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static void ExtractShapeTreeText(ShapeTree shapeTree, StringBuilder sb)
    {
        foreach (var shape in shapeTree.ChildElements)
        {
            if (shape is Shape sp)
            {
                var textBody = sp.TextBody;
                if (textBody is null) continue;

                foreach (var paragraph in textBody.Elements<A.Paragraph>())
                {
                    var text = GetParagraphText(paragraph);
                    if (!string.IsNullOrWhiteSpace(text))
                        sb.AppendLine(text);
                }
            }
            else if (shape is GraphicFrame gf)
            {
                // Tables inside graphic frames
                var table = gf.Descendants<A.Table>().FirstOrDefault();
                if (table is not null)
                {
                    var md = ConvertTableToMarkdown(table);
                    if (!string.IsNullOrEmpty(md))
                    {
                        sb.AppendLine();
                        sb.AppendLine(md);
                    }
                }
            }
            else if (shape is GroupShape gs)
            {
                // Recurse into grouped shapes
                foreach (var child in gs.ChildElements)
                {
                    if (child is Shape groupedShape)
                    {
                        var textBody = groupedShape.TextBody;
                        if (textBody is null) continue;

                        foreach (var paragraph in textBody.Elements<A.Paragraph>())
                        {
                            var text = GetParagraphText(paragraph);
                            if (!string.IsNullOrWhiteSpace(text))
                                sb.AppendLine(text);
                        }
                    }
                }
            }
        }
    }

    private static string GetParagraphText(A.Paragraph paragraph)
    {
        var sb = new StringBuilder();
        foreach (var run in paragraph.Elements<A.Run>())
        {
            var text = run.Text?.Text;
            if (text is not null) sb.Append(text);
        }
        // Also handle A.Field elements (e.g., slide numbers, dates)
        foreach (var field in paragraph.Elements<A.Field>())
        {
            var text = field.Text?.Text;
            if (text is not null) sb.Append(text);
        }
        return sb.ToString();
    }

    private static string ExtractNotesText(NotesSlidePart notesPart)
    {
        var sb = new StringBuilder();
        var shapes = notesPart.NotesSlide?.CommonSlideData?.ShapeTree?.Elements<Shape>();
        if (shapes is null) return "";

        foreach (var shape in shapes)
        {
            var textBody = shape.TextBody;
            if (textBody is null) continue;

            foreach (var paragraph in textBody.Elements<A.Paragraph>())
            {
                var text = GetParagraphText(paragraph);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(text);
                }
            }
        }

        return sb.ToString();
    }

    private static string ConvertTableToMarkdown(A.Table table)
    {
        var rows = table.Elements<A.TableRow>().ToList();
        if (rows.Count == 0) return "";

        var tableData = new List<string[]>();
        var maxCols = 0;

        foreach (var row in rows)
        {
            var cells = row.Elements<A.TableCell>()
                .Select(cell =>
                {
                    var cellText = new StringBuilder();
                    foreach (var p in cell.Elements<A.TextBody>().SelectMany(tb => tb.Elements<A.Paragraph>()))
                    {
                        var text = GetParagraphText(p);
                        if (!string.IsNullOrEmpty(text))
                        {
                            if (cellText.Length > 0) cellText.Append(' ');
                            cellText.Append(text);
                        }
                    }
                    return cellText.ToString();
                })
                .ToArray();

            if (cells.Length > maxCols) maxCols = cells.Length;
            tableData.Add(cells);
        }

        if (maxCols == 0) return "";

        var sb = new StringBuilder();
        for (var i = 0; i < tableData.Count; i++)
        {
            var rowData = tableData[i];
            sb.Append('|');
            for (var j = 0; j < maxCols; j++)
            {
                var cellText = j < rowData.Length ? rowData[j].Replace("|", "\\|") : "";
                sb.Append($" {cellText} |");
            }
            sb.AppendLine();

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
