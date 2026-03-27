namespace FieldCure.DocumentParsers.Pdf.Tests;

[TestClass]
public class PdfParserTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private readonly PdfParser _parser = new();

    private byte[] ReadBytes(string filename)
        => File.ReadAllBytes(Path.Combine(TestDataDir, filename));

    private string ReadText(string filename)
        => _parser.ExtractText(ReadBytes(filename));

    #region Extension support

    [TestMethod]
    public void SupportedExtensions_ContainsPdf()
    {
        CollectionAssert.Contains(_parser.SupportedExtensions.ToList(), ".pdf");
    }

    #endregion

    #region sample_math_equations.pdf — text extraction

    [TestMethod]
    [Description("ExtractText must return non-empty text from a non-trivial PDF.")]
    public void ExtractText_ReturnsNonEmptyText()
    {
        var text = ReadText("sample_math_equations.pdf");

        Assert.IsFalse(string.IsNullOrWhiteSpace(text),
            "ExtractText should return non-empty text.");
    }

    [TestMethod]
    [Description("Each page must be preceded by a '## Page N' header.")]
    public void ExtractText_InsertsPageHeaders()
    {
        var text = ReadText("sample_math_equations.pdf");

        Assert.IsTrue(text.Contains("## Page 1"),
            "Expected '## Page 1' header.");
        Assert.IsTrue(text.Contains("## Page 2"),
            "Expected '## Page 2' header for second page.");
    }

    [TestMethod]
    [Description("Page headers must appear in ascending order.")]
    public void ExtractText_PageHeadersAreOrdered()
    {
        var text = ReadText("sample_math_equations.pdf");

        var pos1 = text.IndexOf("## Page 1", StringComparison.Ordinal);
        var pos2 = text.IndexOf("## Page 2", StringComparison.Ordinal);

        Assert.IsTrue(pos1 >= 0, "'## Page 1' not found.");
        Assert.IsTrue(pos2 >= 0, "'## Page 2' not found.");
        Assert.IsTrue(pos1 < pos2, "'## Page 1' should precede '## Page 2'.");
    }

    [TestMethod]
    [Description("Plain-text content from the document body must be present.")]
    public void ExtractText_ContainsBodyText()
    {
        var text = ReadText("sample_math_equations.pdf");

        Assert.IsTrue(text.Contains("The Gradient"),
            "Expected section title 'The Gradient'.");
        Assert.IsTrue(text.Contains("The impedance function"),
            "Expected prose 'The impedance function'.");
        Assert.IsTrue(text.Contains("The DRT representation"),
            "Expected prose 'The DRT representation'.");
        Assert.IsTrue(text.Contains("The Loewner matrix"),
            "Expected prose 'The Loewner matrix'.");
    }

    [TestMethod]
    [Description("Page 1 content must appear before Page 2 content.")]
    public void ExtractText_ContentOrderMatchesPages()
    {
        var text = ReadText("sample_math_equations.pdf");

        // "The Gradient" is on page 1; "Loewner" equation continuation is on page 2.
        var gradPos   = text.IndexOf("The Gradient",   StringComparison.Ordinal);
        var page2Pos  = text.IndexOf("## Page 2",      StringComparison.Ordinal);

        Assert.IsTrue(gradPos  >= 0, "'The Gradient' not found.");
        Assert.IsTrue(page2Pos >= 0, "'## Page 2' not found.");
        Assert.IsTrue(gradPos < page2Pos,
            "'The Gradient' should appear before '## Page 2' marker.");
    }

    #endregion

    #region sample_math_equations.pdf — image extraction

    [TestMethod]
    [Description("ExtractImages must return one image per PDF page.")]
    public void ExtractImages_ReturnsOneImagePerPage()
    {
        var images = _parser.ExtractImages(ReadBytes("sample_math_equations.pdf"));

        Assert.AreEqual(2, images.Count,
            $"Expected 2 images (one per page), got {images.Count}.");
    }

    [TestMethod]
    [Description("Each rendered image must have non-empty PNG bytes.")]
    public void ExtractImages_ImagesAreNonEmpty()
    {
        var images = _parser.ExtractImages(ReadBytes("sample_math_equations.pdf"));

        foreach (var img in images)
        {
            Assert.IsTrue(img.Data.Length > 0,
                $"Image for '{img.Label}' must have non-zero byte length.");
        }
    }

    [TestMethod]
    [Description("Image labels must follow the 'Page N' convention.")]
    public void ExtractImages_LabelsFollowPageConvention()
    {
        var images = _parser.ExtractImages(ReadBytes("sample_math_equations.pdf"));

        Assert.AreEqual("Page 1", images[0].Label, "First image label should be 'Page 1'.");
        Assert.AreEqual("Page 2", images[1].Label, "Second image label should be 'Page 2'.");
    }

    [TestMethod]
    [Description("Page indices must be zero-based and match the image order.")]
    public void ExtractImages_PageIndicesAreZeroBased()
    {
        var images = _parser.ExtractImages(ReadBytes("sample_math_equations.pdf"));

        Assert.AreEqual(0, images[0].Index, "First image index should be 0.");
        Assert.AreEqual(1, images[1].Index, "Second image index should be 1.");
    }

    [TestMethod]
    [Description("Higher DPI must produce a larger image than lower DPI.")]
    public void ExtractImages_HigherDpiProducesLargerImage()
    {
        var data = ReadBytes("sample_math_equations.pdf");
        var lo = _parser.ExtractImages(data, dpi: 72);
        var hi = _parser.ExtractImages(data, dpi: 150);

        Assert.IsTrue(hi[0].Data.Length > lo[0].Data.Length,
            "150 dpi image should be larger (more bytes) than 72 dpi image.");
    }

    #endregion
}
