using System.Net;
using System.Text;
using System.Text.Json;
using FieldCure.AssistStudio.Tools;

namespace FieldCure.AssistStudio.Tests;

[TestClass]
public class UrlFetchToolTests
{
    #region Helper

    private static UrlFetchTool CreateTool(
        HttpMessageHandler handler,
        int maxContentLength = 8000,
        int maxResponseBytes = 1_048_576)
    {
        var client = new HttpClient(handler);
        return new UrlFetchTool(
            maxContentLength: maxContentLength,
            maxResponseBytes: maxResponseBytes,
            httpClient: client);
    }

    private static JsonElement Params(string url) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new { url })).RootElement;

    private static string GetError(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("error").GetString()!;
    }

    private static string GetContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("content").GetString()!;
    }

    #endregion

    #region Success Cases

    [TestMethod]
    public async Task ExecuteAsync_HtmlPage_ReturnsExtractedText()
    {
        var html = "<html><body><h1>Title</h1><p>Hello World</p></body></html>";
        var handler = new MockHandler(html, "text/html");
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Params("https://example.com"));
        var content = GetContent(result);

        StringAssert.Contains(content, "Title");
        StringAssert.Contains(content, "Hello World");
    }

    [TestMethod]
    public async Task ExecuteAsync_PlainText_ReturnsRawText()
    {
        var text = "Just plain text content.";
        var handler = new MockHandler(text, "text/plain");
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Params("https://example.com/file.txt"));
        var content = GetContent(result);

        Assert.AreEqual(text, content);
    }

    #endregion

    #region Validation Errors

    [TestMethod]
    public async Task ExecuteAsync_MissingUrl_ReturnsError()
    {
        var handler = new MockHandler("ok", "text/html");
        var tool = CreateTool(handler);
        var param = JsonDocument.Parse("{}").RootElement;

        var result = await tool.ExecuteAsync(param);
        StringAssert.Contains(GetError(result), "Missing required parameter");
    }

    [TestMethod]
    public async Task ExecuteAsync_NonHttpScheme_ReturnsError()
    {
        var handler = new MockHandler("ok", "text/html");
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Params("ftp://example.com/file"));
        StringAssert.Contains(GetError(result), "unsupported scheme");
    }

    [TestMethod]
    public async Task ExecuteAsync_UnsupportedContentType_ReturnsError()
    {
        var handler = new MockHandler("binary", "application/pdf");
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Params("https://example.com/doc.pdf"));
        StringAssert.Contains(GetError(result), "Unsupported content type");
    }

    [TestMethod]
    public async Task ExecuteAsync_HttpError_ReturnsError()
    {
        var handler = new MockHandler(statusCode: HttpStatusCode.NotFound);
        var tool = CreateTool(handler);

        var result = await tool.ExecuteAsync(Params("https://example.com/missing"));
        StringAssert.Contains(GetError(result), "404");
    }

    #endregion

    #region Size Limits

    [TestMethod]
    public async Task ExecuteAsync_RespectsMaxContentLength()
    {
        var html = "<p>" + new string('A', 500) + "</p>";
        var handler = new MockHandler(html, "text/html");
        var tool = CreateTool(handler, maxContentLength: 100);

        var result = await tool.ExecuteAsync(Params("https://example.com"));
        var content = GetContent(result);

        Assert.IsTrue(content.Contains("[Truncated]"), "Should be truncated");
    }

    [TestMethod]
    public async Task ExecuteAsync_ContentLengthExceedsMax_ReturnsError()
    {
        var handler = new MockHandler("ok", "text/html", contentLength: 2_000_000);
        var tool = CreateTool(handler, maxResponseBytes: 1_048_576);

        var result = await tool.ExecuteAsync(Params("https://example.com"));
        StringAssert.Contains(GetError(result), "exceeds size limit");
    }

    #endregion

    #region SSRF Prevention — IsPrivateOrLoopback

    [TestMethod]
    [DataRow("127.0.0.1", true)]
    [DataRow("127.0.0.2", true)]
    [DataRow("10.0.0.1", true)]
    [DataRow("10.255.255.255", true)]
    [DataRow("172.16.0.1", true)]
    [DataRow("172.31.255.255", true)]
    [DataRow("192.168.0.1", true)]
    [DataRow("192.168.255.255", true)]
    [DataRow("169.254.1.1", true)]
    [DataRow("8.8.8.8", false)]
    [DataRow("1.1.1.1", false)]
    [DataRow("172.15.0.1", false)]
    [DataRow("172.32.0.1", false)]
    [DataRow("192.169.0.1", false)]
    public void IsPrivateOrLoopback_IPv4(string ip, bool expected)
    {
        var address = IPAddress.Parse(ip);
        Assert.AreEqual(expected, UrlFetchTool.IsPrivateOrLoopback(address),
            $"Expected {ip} to be {(expected ? "private" : "public")}");
    }

    [TestMethod]
    public void IsPrivateOrLoopback_IPv6Loopback()
    {
        Assert.IsTrue(UrlFetchTool.IsPrivateOrLoopback(IPAddress.IPv6Loopback)); // ::1
    }

    #endregion

    #region MockHandler

    private sealed class MockHandler : HttpMessageHandler
    {
        private readonly string _content;
        private readonly string _contentType;
        private readonly HttpStatusCode _statusCode;
        private readonly long? _contentLength;

        public MockHandler(
            string content = "",
            string contentType = "text/html",
            HttpStatusCode statusCode = HttpStatusCode.OK,
            long? contentLength = null)
        {
            _content = content;
            _contentType = contentType;
            _statusCode = statusCode;
            _contentLength = contentLength;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, _contentType)
            };

            if (_contentLength.HasValue)
                response.Content.Headers.ContentLength = _contentLength.Value;

            return Task.FromResult(response);
        }
    }

    #endregion
}
