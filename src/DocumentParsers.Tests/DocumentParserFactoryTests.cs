namespace FieldCure.DocumentParsers.Tests;

[TestClass]
public class DocumentParserFactoryTests
{
    [TestMethod]
    [DataRow(".docx")]
    [DataRow(".DOCX")]
    public void GetParser_Docx_ReturnsDocxParser(string ext)
    {
        var parser = DocumentParserFactory.GetParser(ext);
        Assert.IsInstanceOfType<DocxParser>(parser);
    }

    [TestMethod]
    [DataRow(".hwpx")]
    [DataRow(".HWPX")]
    public void GetParser_Hwpx_ReturnsHwpxParser(string ext)
    {
        var parser = DocumentParserFactory.GetParser(ext);
        Assert.IsInstanceOfType<HwpxParser>(parser);
    }

    [TestMethod]
    [DataRow(".pdf")]
    [DataRow(".txt")]
    [DataRow(".xyz")]
    [DataRow("")]
    public void GetParser_Unsupported_ReturnsNull(string ext)
    {
        var parser = DocumentParserFactory.GetParser(ext);
        Assert.IsNull(parser);
    }

    [TestMethod]
    public void SupportedExtensions_ContainsExpected()
    {
        var extensions = DocumentParserFactory.SupportedExtensions.ToList();
        CollectionAssert.Contains(extensions, ".docx");
        CollectionAssert.Contains(extensions, ".hwpx");
    }
}
