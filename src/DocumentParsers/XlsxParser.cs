using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace FieldCure.DocumentParsers;

/// <summary>
/// Extracts text from Excel (.xlsx) files using OpenXML SDK.
/// Sheets are output as markdown tables for LLM / RAG consumption.
/// </summary>
public sealed class XlsxParser : IDocumentParser
{
    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions => [".xlsx"];

    /// <inheritdoc />
    public string ExtractText(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var doc = SpreadsheetDocument.Open(stream, false);
        var workbookPart = doc.WorkbookPart;
        if (workbookPart is null) return "";

        // Load shared string table for cell value resolution
        var sst = workbookPart.SharedStringTablePart?.SharedStringTable;
        var sharedStrings = sst?.Elements<SharedStringItem>()
            .Select(s => s.InnerText)
            .ToArray() ?? [];

        var sheets = workbookPart.Workbook?.Sheets?.Elements<Sheet>().ToList();
        if (sheets is null or { Count: 0 }) return "";

        var sb = new StringBuilder();

        foreach (var sheet in sheets)
        {
            var worksheetPart = workbookPart.GetPartById(sheet.Id!) as WorksheetPart;
            if (worksheetPart is null) continue;

            var sheetData = worksheetPart.Worksheet?.GetFirstChild<SheetData>();
            if (sheetData is null) continue;

            var rows = sheetData.Elements<Row>().ToList();
            if (rows.Count == 0) continue;

            // Sheet header
            if (sb.Length > 0) { sb.AppendLine(); sb.AppendLine(); }
            var sheetName = sheet.Name?.Value ?? "Sheet";
            sb.AppendLine($"## Sheet: {sheetName}");
            sb.AppendLine();

            // Collect all row data
            var tableData = new List<string[]>();
            var maxCols = 0;

            foreach (var row in rows)
            {
                var cells = row.Elements<Cell>().ToList();
                if (cells.Count == 0) continue;

                // Determine column count from cell references
                var colCount = cells.Max(c => GetColumnIndex(c.CellReference!)) + 1;
                if (colCount > maxCols) maxCols = colCount;

                var rowValues = new string[colCount];
                foreach (var cell in cells)
                {
                    var colIndex = GetColumnIndex(cell.CellReference!);
                    rowValues[colIndex] = GetCellValue(cell, sharedStrings);
                }

                tableData.Add(rowValues);
            }

            if (maxCols == 0) continue;

            // Build markdown table
            for (var i = 0; i < tableData.Count; i++)
            {
                var rowValues = tableData[i];
                sb.Append('|');
                for (var j = 0; j < maxCols; j++)
                {
                    var cellText = j < rowValues.Length ? (rowValues[j] ?? "").Replace("|", "\\|") : "";
                    sb.Append($" {cellText} |");
                }
                sb.AppendLine();

                // Separator after first row (header)
                if (i == 0)
                {
                    sb.Append('|');
                    for (var j = 0; j < maxCols; j++)
                        sb.Append(" --- |");
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string GetCellValue(Cell cell, string[] sharedStrings)
    {
        var value = cell.CellValue?.InnerText;
        if (value is null) return cell.InnerText ?? "";

        // SharedString reference
        if (cell.DataType?.Value == CellValues.SharedString &&
            int.TryParse(value, out var index) &&
            index >= 0 && index < sharedStrings.Length)
        {
            return sharedStrings[index];
        }

        // InlineString
        if (cell.DataType?.Value == CellValues.InlineString)
            return cell.InnerText ?? "";

        return value;
    }

    /// <summary>
    /// Converts a cell reference like "B3" or "AA1" to a zero-based column index.
    /// </summary>
    private static int GetColumnIndex(string cellReference)
    {
        var col = 0;
        foreach (var c in cellReference)
        {
            if (!char.IsLetter(c)) break;
            col = col * 26 + (char.ToUpper(c) - 'A' + 1);
        }
        return col - 1;
    }
}
