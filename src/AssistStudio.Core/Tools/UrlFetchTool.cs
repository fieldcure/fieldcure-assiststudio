using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Tools;

/// <summary>
/// Fetches the content of a web page URL and returns extracted text.
/// Includes SSRF prevention via DNS resolution checks.
/// </summary>
public class UrlFetchTool : IAssistTool
{
    #region Fields

    private readonly HttpClient _httpClient;
    private readonly int _maxContentLength;
    private readonly int _maxResponseBytes;
    private readonly TimeSpan _timeout;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of <see cref="UrlFetchTool"/>.
    /// </summary>
    /// <param name="maxContentLength">Maximum length of the extracted text to return. Default is 8000.</param>
    /// <param name="timeout">HTTP request timeout. Default is 10 seconds.</param>
    /// <param name="maxResponseBytes">Maximum response body size in bytes. Default is 1 MB.</param>
    /// <param name="httpClient">Optional externally managed <see cref="HttpClient"/>. If null, an internal one is created.</param>
    public UrlFetchTool(
        int maxContentLength = 8000,
        TimeSpan? timeout = null,
        int maxResponseBytes = 1_048_576,
        HttpClient? httpClient = null)
    {
        _maxContentLength = maxContentLength;
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _maxResponseBytes = maxResponseBytes;

        if (httpClient is not null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AssistStudio/1.0");
        }
    }

    #endregion

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "fetch_url";

    /// <inheritdoc/>
    public string DisplayName => "Fetch URL";

    /// <inheritdoc/>
    public string Description =>
        "Fetches the content of a web page and returns extracted text. " +
        "Use when the user shares a URL or asks about web page content.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "The URL to fetch (http or https only)"
            }
          },
          "required": ["url"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var urlString = parameters.TryGetProperty("url", out var urlEl)
            ? urlEl.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(urlString))
            return Error("Missing required parameter: url");

        // Validate URL scheme
        if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Error($"Invalid URL or unsupported scheme (only http/https allowed): {urlString}");
        }

        // SSRF: DNS resolve and check for private/loopback IPs
        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
        }
        catch (SocketException)
        {
            return Error($"Could not resolve hostname: {uri.Host}");
        }

        if (addresses.Length == 0)
            return Error($"Could not resolve hostname: {uri.Host}");

        if (addresses.Any(IsPrivateOrLoopback))
            return Error("URL points to a private or local address");

        // Fetch
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_timeout);

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error($"Request timed out after {_timeout.TotalSeconds:0}s");
        }
        catch (HttpRequestException ex)
        {
            return Error($"HTTP request failed: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
            return Error($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");

        // Check Content-Length early
        if (response.Content.Headers.ContentLength > _maxResponseBytes)
            return Error($"Response exceeds size limit ({_maxResponseBytes / (1024 * 1024)}MB)");

        // Check Content-Type
        var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        var isHtml = contentType is "text/html" or "application/xhtml+xml";
        var isPlainText = contentType is "text/plain";

        if (!isHtml && !isPlainText)
            return Error($"Unsupported content type: {contentType}");

        // Read response body with size limit
        string body;
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            var buffer = new char[_maxResponseBytes];
            var read = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            body = new string(buffer, 0, read);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error($"Request timed out after {_timeout.TotalSeconds:0}s");
        }

        // Extract text
        var text = isHtml
            ? HtmlTextExtractor.Extract(body, _maxContentLength)
            : Truncate(body, _maxContentLength);

        if (string.IsNullOrWhiteSpace(text))
            return Error("Page returned no readable text content");

        return JsonSerializer.Serialize(new
        {
            url = uri.AbsoluteUri,
            content = text
        });
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Determines whether an IP address is a loopback or private network address.
    /// </summary>
    internal static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        // Map IPv6-mapped IPv4 to IPv4 for consistent checking
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => bytes[0] switch
            {
                10 => true,                                         // 10.0.0.0/8
                172 => bytes[1] >= 16 && bytes[1] <= 31,            // 172.16.0.0/12
                192 => bytes[1] == 168,                             // 192.168.0.0/16
                169 => bytes[1] == 254,                             // 169.254.0.0/16 (link-local)
                _ => false
            },
            AddressFamily.InterNetworkV6 => address.IsIPv6LinkLocal // fe80::/10
                                            || address.IsIPv6SiteLocal,
            _ => false
        };
    }

    private static string Truncate(string text, int maxLength)
    {
        text = text.Trim();
        if (text.Length <= maxLength)
            return text;
        return string.Concat(text.AsSpan(0, maxLength), "\n[Truncated]");
    }

    private static string Error(string message) =>
        JsonSerializer.Serialize(new { error = message });

    #endregion
}
