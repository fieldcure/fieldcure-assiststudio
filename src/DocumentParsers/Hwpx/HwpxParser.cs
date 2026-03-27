using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Extracts plain text from HWPX files (Korean standard document format, KS X 6101 / OWPML).
/// HWPX is a ZIP archive containing XML sections. Text is extracted at paragraph level (hp:p),
/// and tables (hp:tbl) are converted to markdown format for LLM comprehension.
/// </summary>
public sealed class HwpxParser : IDocumentParser
{
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions => [".hwpx"];

    /// <inheritdoc />
    public string ExtractText(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        // Collect section entries sorted by name (section0.xml, section1.xml, ...)
        var sectionEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("Contents/section", StringComparison.OrdinalIgnoreCase)
                     && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sectionEntries.Count == 0) return "";

        var sb = new StringBuilder();

        foreach (var entry in sectionEntries)
        {
            using var entryStream = entry.Open();
            var xdoc = XDocument.Load(entryStream);

            // Process from section elements, or root as fallback
            var sectionElements = xdoc.Descendants()
                .Where(e => e.Name.LocalName == "sec");

            foreach (var sec in sectionElements)
                ProcessBlockElements(sec, sb);

            if (!sectionElements.Any() && xdoc.Root is not null)
                ProcessBlockElements(xdoc.Root, sb);
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Processes block-level elements (paragraphs and tables) from a parent HWPX element.
    /// In HWPX, tables (hp:tbl) are embedded inside paragraphs (hp:p > hp:run > hp:tbl),
    /// so paragraphs are checked for embedded tables as well.
    /// </summary>
    private static void ProcessBlockElements(XElement parent, StringBuilder sb)
    {
        foreach (var child in parent.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName == "p")
            {
                // Check for embedded tables inside this paragraph (hp:p > hp:run > hp:tbl)
                var embeddedTables = child.Descendants()
                    .Where(e => e.Name.LocalName == "tbl")
                    .ToList();

                if (embeddedTables.Count > 0)
                {
                    // Extract paragraph text (excluding table cell text)
                    var paraText = ExtractParagraphTextExcludingTables(child);
                    if (!string.IsNullOrWhiteSpace(paraText))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(paraText);
                    }

                    // Then render each embedded table
                    foreach (var tbl in embeddedTables)
                    {
                        var tableText = ConvertTableToMarkdown(tbl);
                        if (!string.IsNullOrEmpty(tableText))
                        {
                            if (sb.Length > 0) { sb.AppendLine(); sb.AppendLine(); }
                            sb.Append(tableText);
                        }
                    }
                }
                else
                {
                    // Simple paragraph: concatenate all hp:t text within runs
                    var paraText = ExtractParagraphText(child);
                    if (!string.IsNullOrWhiteSpace(paraText))
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.Append(paraText);
                    }
                }
            }
            else if (localName == "tbl")
            {
                // Standalone table (direct child)
                var tableText = ConvertTableToMarkdown(child);
                if (!string.IsNullOrEmpty(tableText))
                {
                    if (sb.Length > 0) { sb.AppendLine(); sb.AppendLine(); }
                    sb.Append(tableText);
                }
            }
            else if (localName is "sec" or "subList" or "cell" or "drawText")
            {
                // Recurse into container elements that may hold paragraphs/tables
                ProcessBlockElements(child, sb);
            }
        }
    }

    /// <summary>
    /// Extracts and concatenates all text from hp:t elements within a single paragraph (hp:p).
    /// Equation elements (hp:equation) are converted to [math: ...] notation.
    /// Multiple runs in the same paragraph are joined without separator.
    /// </summary>
    private static string ExtractParagraphText(XElement paragraph)
    {
        // Check if paragraph contains equations
        var hasEquations = paragraph.Descendants()
            .Any(e => e.Name.LocalName == "equation");

        if (!hasEquations)
        {
            // Fast path: no equations, just concatenate text nodes
            var textNodes = paragraph.Descendants()
                .Where(e => e.Name.LocalName == "t");
            var plainSb = new StringBuilder();
            foreach (var t in textNodes)
                plainSb.Append(t.Value);
            return plainSb.ToString();
        }

        // Slow path: process children to interleave text and equations
        var sb = new StringBuilder();
        ExtractWithEquations(paragraph, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Recursively extracts text and equation elements, converting equations to [math: ...].
    /// </summary>
    private static void ExtractWithEquations(XElement element, StringBuilder sb)
    {
        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName == "equation")
            {
                // Extract script text from equation element
                var script = child.Elements()
                    .FirstOrDefault(e => e.Name.LocalName == "script");
                if (script is not null)
                {
                    var latex = HancomMathNormalizer.ToLaTeX(script.Value);
                    if (!string.IsNullOrWhiteSpace(latex))
                        sb.Append($"[math: {latex}]");
                }
            }
            else if (localName == "t")
            {
                sb.Append(child.Value);
            }
            else if (localName is "tbl")
            {
                // Skip tables — handled separately
            }
            else
            {
                // Recurse into runs, subList, etc.
                ExtractWithEquations(child, sb);
            }
        }
    }

    /// <summary>
    /// Extracts text from a paragraph while excluding any text that belongs to embedded tables.
    /// This prevents table cell text from being duplicated outside the markdown table.
    /// </summary>
    private static string ExtractParagraphTextExcludingTables(XElement paragraph)
    {
        // Collect all tbl elements to build an exclusion set
        var tableElements = new HashSet<XElement>(
            paragraph.Descendants().Where(e => e.Name.LocalName == "tbl"));

        var textNodes = paragraph.Descendants()
            .Where(e => e.Name.LocalName == "t"
                     && !e.Ancestors().Any(a => tableElements.Contains(a)));

        var sb = new StringBuilder();
        foreach (var t in textNodes)
            sb.Append(t.Value);
        return sb.ToString();
    }

    /// <summary>
    /// Converts an HWPX table element (hp:tbl) to a markdown-formatted table string.
    /// The first row is treated as the header row.
    /// </summary>
    private static string ConvertTableToMarkdown(XElement table)
    {
        var rows = table.Elements()
            .Where(e => e.Name.LocalName == "tr")
            .ToList();

        if (rows.Count == 0) return "";

        var tableData = new List<string[]>();
        var maxCols = 0;

        foreach (var row in rows)
        {
            var cells = row.Elements()
                .Where(e => e.Name.LocalName == "tc")
                .Select(tc =>
                {
                    // A cell may contain paragraphs at any depth (e.g. tc > subList > p in HWPX)
                    var cellParagraphs = tc.Descendants()
                        .Where(e => e.Name.LocalName == "p")
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
