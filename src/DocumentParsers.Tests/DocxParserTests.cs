namespace FieldCure.DocumentParsers.Tests;

[TestClass]
public class DocxParserTests
{
    private static readonly string TestDataDir =
        Path.Combine(AppContext.BaseDirectory, "TestData");

    private readonly DocxParser _parser = new();

    [TestMethod]
    public void SupportedExtensions_ContainsDocx()
    {
        CollectionAssert.Contains(_parser.SupportedExtensions.ToList(), ".docx");
    }

    [TestMethod]
    public void ExtractText_WithSampleFile_ExtractsParagraphs()
    {
        var path = Path.Combine(TestDataDir, "sample.docx");
        if (!File.Exists(path))
            Assert.Inconclusive("sample.docx not found in TestData folder.");

        var data = File.ReadAllBytes(path);
        var text = _parser.ExtractText(data);

        Assert.IsFalse(string.IsNullOrWhiteSpace(text), "Extracted text should not be empty.");
    }

    [TestMethod]
    public void ExtractText_WithSampleFile_ContainsMarkdownTable()
    {
        var path = Path.Combine(TestDataDir, "sample.docx");
        if (!File.Exists(path))
            Assert.Inconclusive("sample.docx not found in TestData folder.");

        var data = File.ReadAllBytes(path);
        var text = _parser.ExtractText(data);

        // Markdown table should contain pipe characters and separator row
        Assert.IsTrue(text.Contains('|'), "Table should be converted to markdown with pipe characters.");
        Assert.IsTrue(text.Contains("---"), "Table should have markdown separator row.");
    }

    [TestMethod]
    public void ExtractText_EmptyDocument_ReturnsEmptyString()
    {
        var path = Path.Combine(TestDataDir, "empty.docx");
        if (!File.Exists(path))
            Assert.Inconclusive("empty.docx not found in TestData folder.");

        var data = File.ReadAllBytes(path);
        var text = _parser.ExtractText(data);

        Assert.AreEqual("", text);
    }
}
