namespace FieldCure.DocumentParsers.Tests;

[TestClass]
public class HwpxParserTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private readonly HwpxParser _parser = new();

    private string ReadText(string filename)
    {
        var data = File.ReadAllBytes(Path.Combine(TestDataDir, filename));
        return _parser.ExtractText(data);
    }

    #region Extension support

    [TestMethod]
    public void SupportedExtensions_ContainsHwpx()
    {
        CollectionAssert.Contains(_parser.SupportedExtensions.ToList(), ".hwpx");
    }

    #endregion

    #region simple_text.hwpx — basic paragraphs

    [TestMethod]
    public void SimpleText_ExtractsThreeLines()
    {
        var text = ReadText("simple_text.hwpx");
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.IsTrue(lines.Length >= 3, $"Expected at least 3 lines, got {lines.Length}.");
    }

    #endregion

    #region multiple_runs.hwpx — bold/italic runs merged into one line

    [TestMethod]
    public void MultipleRuns_MergesRunsIntoSingleLine()
    {
        var text = ReadText("multiple_runs.hwpx");

        Assert.IsTrue(
            text.Contains("This paragraph contains bold text and italic text mixed together."),
            $"Runs should be merged. Actual:\n{text}");
    }

    #endregion

    #region with_table.hwpx — text + markdown table + text

    [TestMethod]
    public void WithTable_ContainsMarkdownTable()
    {
        var text = ReadText("with_table.hwpx");

        Assert.IsTrue(text.Contains('|'), "Table should be converted to markdown with pipe characters.");
        Assert.IsTrue(text.Contains("---"), "Table should have markdown separator row.");
    }

    [TestMethod]
    public void WithTable_Has3x4Table()
    {
        var text = ReadText("with_table.hwpx");

        var lines = text.Split('\n');
        var separatorLine = lines.FirstOrDefault(l => l.Contains("---") && l.Contains('|'));

        Assert.IsNotNull(separatorLine, "Should have a markdown separator row.");

        var pipeCount = separatorLine.Count(c => c == '|');
        Assert.IsTrue(pipeCount >= 4, $"Expected 3+ columns (4+ pipes), got {pipeCount} pipes.");
    }

    #endregion

    #region multiple_tables.hwpx — two tables

    [TestMethod]
    public void MultipleTables_ExtractsBothTables()
    {
        var text = ReadText("multiple_tables.hwpx");

        var lines = text.Split('\n');
        var separatorRows = lines.Where(l => l.Contains("---") && l.Contains('|')).ToList();

        Assert.IsTrue(separatorRows.Count >= 2,
            $"Expected at least 2 separator rows for 2 tables, got {separatorRows.Count}.");
    }

    #endregion

    #region empty.hwpx — empty document

    [TestMethod]
    public void EmptyDocument_ReturnsEmptyOrWhitespace()
    {
        var text = ReadText("empty.hwpx");
        Assert.IsTrue(string.IsNullOrWhiteSpace(text), $"Expected empty, got: '{text}'");
    }

    #endregion

    #region pipe_in_table.hwpx — pipe character inside cell

    [TestMethod]
    public void PipeInTable_EscapesPipeInCellContent()
    {
        var text = ReadText("pipe_in_table.hwpx");

        Assert.IsTrue(text.Contains("\\|"),
            $"Pipe in cell content should be escaped. Actual:\n{text}");
    }

    #endregion
}
