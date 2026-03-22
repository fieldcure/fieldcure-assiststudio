namespace FieldCure.DocumentParsers.Tests;

[TestClass]
public class HwpxParserTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private readonly HwpxParser _parser = new();

    [TestMethod]
    public void SupportedExtensions_ContainsHwpx()
    {
        CollectionAssert.Contains(_parser.SupportedExtensions.ToList(), ".hwpx");
    }

    [TestMethod]
    public void ExtractText_WithSampleFile_ExtractsParagraphs()
    {
        var path = Path.Combine(TestDataDir, "sample.hwpx");
        if (!File.Exists(path))
            Assert.Inconclusive("sample.hwpx not found in TestData folder.");

        var data = File.ReadAllBytes(path);
        var text = _parser.ExtractText(data);

        Assert.IsFalse(string.IsNullOrWhiteSpace(text), "Extracted text should not be empty.");
    }

    [TestMethod]
    public void ExtractText_WithSampleFile_ContainsMarkdownTable()
    {
        var path = Path.Combine(TestDataDir, "sample.hwpx");
        if (!File.Exists(path))
            Assert.Inconclusive("sample.hwpx not found in TestData folder.");

        var data = File.ReadAllBytes(path);
        var text = _parser.ExtractText(data);

        Assert.IsTrue(text.Contains('|'), "Table should be converted to markdown with pipe characters.");
        Assert.IsTrue(text.Contains("---"), "Table should have markdown separator row.");
    }
}
