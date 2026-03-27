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

    #region sample_math_equations.hwpx — math equation extraction

    // Helper: returns all lines of the extracted text.
    private string[] MathLines() =>
        ReadText("sample_math_equations.hwpx").Split('\n');

    [TestMethod]
    [Description("hp:equation/hp:script elements must be emitted as [math: ...] blocks.")]
    public void MathEquations_ContainsMathBlockFormat()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains("[math:"),
            $"Expected [math: ...] blocks in output.\nActual:\n{text}");
    }

    [TestMethod]
    [Description("All 7 hp:equation elements must each produce exactly one [math: ...] line.")]
    public void MathEquations_ExtractsSevenEquations()
    {
        var count = MathLines().Count(l => l.Contains("[math:"));

        Assert.AreEqual(7, count, $"Expected 7 [math:] lines, got {count}.");
    }

    [TestMethod]
    [Description("OVER keyword (Hancom fraction) must convert to \\frac{}{}.")]
    public void MathEquations_FracFromOVERKeyword()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains(@"\frac"),
            $@"Expected \frac (from OVER conversion).\nActual:\n{text}");
    }

    [TestMethod]
    [Description("hat keyword must convert to \\widehat{}.")]
    public void MathEquations_WidehatFromHatKeyword()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains(@"\widehat"),
            $@"Expected \widehat (from hat conversion).\nActual:\n{text}");
    }

    [TestMethod]
    [Description("sum with _ and ^ limits must be present in the H(s) equation.")]
    public void MathEquations_SumWithSubscriptSuperscript()
    {
        var text = ReadText("sample_math_equations.hwpx");

        // H(s) = sum_{k=1}^{N} frac{...}
        Assert.IsTrue(text.Contains(@"\sum _"),
            $@"Expected \sum _ (sum with subscript limit).\nActual:\n{text}");
        Assert.IsTrue(text.Contains(@"\frac{ { { γ } }"),
            $@"Expected \frac with γ numerator in H(s) equation.\nActual:\n{text}");
    }

    [TestMethod]
    [Description("LEFT/RIGHT keywords must convert to \\left and \\right delimiters.")]
    public void MathEquations_LeftRightDelimiters()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains(@"\left ("),
            $@"Expected \left ( delimiter.\nActual:\n{text}");
        Assert.IsTrue(text.Contains(@"\right )"),
            $@"Expected \right ) delimiter.\nActual:\n{text}");
    }

    [TestMethod]
    [Description("UNDEROVER ∫ pattern must convert to \\int with _ and ^ limits.")]
    public void MathEquations_IntegralWithLimits()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains(@"\int _"),
            $@"Expected \int _ (integral with lower limit).\nActual:\n{text}");
        Assert.IsTrue(text.Contains("∞"),
            $@"Expected ∞ (upper limit of integral).\nActual:\n{text}");
    }

    [TestMethod]
    [Description("Absolute-value bars LEFT | … RIGHT | must produce \\left | … \\right |.")]
    public void MathEquations_AbsoluteValueBars()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains(@"\left |"),
            $@"Expected \left | (absolute value open bar).\nActual:\n{text}");
        Assert.IsTrue(text.Contains(@"\right |"),
            $@"Expected \right | (absolute value close bar).\nActual:\n{text}");
    }

    [TestMethod]
    [Description("Plain text paragraphs surrounding equations must be preserved.")]
    public void MathEquations_SurroundingTextPreserved()
    {
        var text = ReadText("sample_math_equations.hwpx");

        Assert.IsTrue(text.Contains("The impedance function"),
            "Expected surrounding text 'The impedance function' to be preserved.");
        Assert.IsTrue(text.Contains("The DRT representation"),
            "Expected surrounding text 'The DRT representation' to be preserved.");
        Assert.IsTrue(text.Contains("The Loewner matrix"),
            "Expected surrounding text 'The Loewner matrix' to be preserved.");
    }

    [TestMethod]
    [Description("Text immediately before an equation block must appear before it in the output " +
                 "(e.g., 'The impedance function … is defined as:' precedes the K_RL equation).")]
    public void MathEquations_TextBeforeEquationPreservesOrder()
    {
        var text = ReadText("sample_math_equations.hwpx");

        // "The impedance function" (line 14) must appear before the K_RL equation (line 15).
        var textPos = text.IndexOf("The impedance function", StringComparison.Ordinal);
        var mathPos = text.IndexOf(@"[math: { { K } } _ { { RL } }", StringComparison.Ordinal);

        Assert.IsTrue(textPos >= 0, "'The impedance function' not found in output.");
        Assert.IsTrue(mathPos >= 0,  "K_RL equation not found in output.");
        Assert.IsTrue(textPos < mathPos,
            $"'The impedance function' (pos {textPos}) should appear before the K_RL equation (pos {mathPos}).");
    }

    [TestMethod]
    [Description("'The DRT representation … integral form:' line must appear before the Z(ω) integral equation.")]
    public void MathEquations_IntroTextBeforeIntegralEquation()
    {
        var text = ReadText("sample_math_equations.hwpx");

        var textPos = text.IndexOf("The DRT representation", StringComparison.Ordinal);
        var mathPos = text.IndexOf(@"\int _", StringComparison.Ordinal);

        Assert.IsTrue(textPos >= 0, "'The DRT representation' not found.");
        Assert.IsTrue(mathPos >= 0,  @"\int _ not found.");
        Assert.IsTrue(textPos < mathPos,
            $"Intro text (pos {textPos}) should precede the integral equation (pos {mathPos}).");
    }

    #endregion
}
