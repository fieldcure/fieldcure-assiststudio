using FieldCure.AssistStudio.Core.Helpers;

namespace FieldCure.AssistStudio.Core.Tests;

/// <summary>
/// Tests HTML-to-text extraction behavior, including stripping unsafe or non-content elements.
/// </summary>
[TestClass]
public class HtmlTextExtractorTests
{
    [TestMethod]
    public void Extract_RemovesScriptBlocks()
    {
        var html = "<p>Hello</p><script>alert('xss');</script><p>World</p>";
        var result = HtmlTextExtractor.Extract(html);

        Assert.IsFalse(result.Contains("alert"));
        StringAssert.Contains(result, "Hello");
        StringAssert.Contains(result, "World");
    }

    [TestMethod]
    public void Extract_RemovesStyleBlocks()
    {
        var html = "<style>body { color: red; }</style><p>Content</p>";
        var result = HtmlTextExtractor.Extract(html);

        Assert.IsFalse(result.Contains("color"));
        StringAssert.Contains(result, "Content");
    }

    [TestMethod]
    public void Extract_RemovesHeadBlock()
    {
        var html = "<html><head><title>Title</title><meta charset='utf-8'></head><body><p>Body</p></body></html>";
        var result = HtmlTextExtractor.Extract(html);

        Assert.IsFalse(result.Contains("Title"));
        Assert.IsFalse(result.Contains("charset"));
        StringAssert.Contains(result, "Body");
    }

    [TestMethod]
    public void Extract_RemovesHtmlComments()
    {
        var html = "<p>Before</p><!-- secret comment --><p>After</p>";
        var result = HtmlTextExtractor.Extract(html);

        Assert.IsFalse(result.Contains("secret"));
        StringAssert.Contains(result, "Before");
        StringAssert.Contains(result, "After");
    }

    [TestMethod]
    public void Extract_DecodesHtmlEntities()
    {
        var html = "<p>A &amp; B &lt; C &gt; D &quot;E&quot; &#39;F&#39; &#x27;G&#x27;</p>";
        var result = HtmlTextExtractor.Extract(html);

        StringAssert.Contains(result, "A & B");
        StringAssert.Contains(result, "< C >");
        StringAssert.Contains(result, "\"E\"");
        StringAssert.Contains(result, "'F'");
    }

    [TestMethod]
    public void Extract_ReplacesBlockElementsWithNewlines()
    {
        var html = "<p>First</p><p>Second</p><div>Third</div><br/>Fourth";
        var result = HtmlTextExtractor.Extract(html);

        // Each block element should produce a line break
        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.IsTrue(lines.Length >= 3, $"Expected at least 3 lines, got {lines.Length}: '{result}'");
    }

    [TestMethod]
    public void Extract_CollapsesWhitespace()
    {
        var html = "<p>  Lots   of    spaces  </p>\n\n\n\n\n<p>After gaps</p>";
        var result = HtmlTextExtractor.Extract(html);

        Assert.IsFalse(result.Contains("   "), "Should not contain triple spaces");
        Assert.IsFalse(result.Contains("\n\n\n"), "Should not contain triple newlines");
    }

    [TestMethod]
    public void Extract_TruncatesAtMaxLength()
    {
        var html = "<p>" + new string('A', 200) + "</p>";
        var result = HtmlTextExtractor.Extract(html, maxLength: 50);

        Assert.IsTrue(result.Length <= 50 + "\n[Truncated]".Length + 5);
        StringAssert.EndsWith(result, "[Truncated]");
    }

    [TestMethod]
    public void Extract_ReturnsEmptyForEmptyInput()
    {
        Assert.AreEqual(string.Empty, HtmlTextExtractor.Extract(""));
        Assert.AreEqual(string.Empty, HtmlTextExtractor.Extract("   "));
        Assert.AreEqual(string.Empty, HtmlTextExtractor.Extract(null!));
    }

    [TestMethod]
    public void Extract_PassesThroughPlainText()
    {
        var text = "Just plain text, no HTML here.";
        var result = HtmlTextExtractor.Extract(text);
        Assert.AreEqual(text, result);
    }

    [TestMethod]
    public void Extract_HandlesNestedTags()
    {
        var html = "<div><ul><li>Item 1</li><li>Item 2</li></ul></div>";
        var result = HtmlTextExtractor.Extract(html);

        StringAssert.Contains(result, "Item 1");
        StringAssert.Contains(result, "Item 2");
    }
}
