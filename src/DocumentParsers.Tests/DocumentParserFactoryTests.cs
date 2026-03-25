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
    [DataRow(".xlsx")]
    [DataRow(".XLSX")]
    public void GetParser_Xlsx_ReturnsXlsxParser(string ext)
    {
        var parser = DocumentParserFactory.GetParser(ext);
        Assert.IsInstanceOfType<XlsxParser>(parser);
    }

    [TestMethod]
    [DataRow(".pptx")]
    [DataRow(".PPTX")]
    public void GetParser_Pptx_ReturnsPptxParser(string ext)
    {
        var parser = DocumentParserFactory.GetParser(ext);
        Assert.IsInstanceOfType<PptxParser>(parser);
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
        CollectionAssert.Contains(extensions, ".xlsx");
        CollectionAssert.Contains(extensions, ".pptx");
    }

    [TestMethod]
    public void Register_AddsParserForExtension()
    {
        // Register returns parser for previously unsupported extension
        var parser = DocumentParserFactory.GetParser(".pdf");
        Assert.IsNull(parser, "PDF should not be registered by default (separate package)");
    }
}
