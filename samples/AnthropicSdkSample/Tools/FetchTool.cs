using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FieldCure.Ai.Providers.Models;

namespace AnthropicSdkSample.Tools;

/// <summary>
/// HTTP fetch tool for the SDK sample. Demonstrates the minimum-viable shape of an
/// <see cref="IAssistTool"/> consumer-side: declare a name/description/schema, run
/// inside <see cref="ExecuteAsync"/>, and return a JSON string the model can read.
/// </summary>
/// <remarks>
/// Adapted from <c>FieldCure.Mcp.Essentials.HttpRequestTool</c>. Defenses kept:
/// DNS-resolve SSRF guard against private IPs, per-request timeout, hard 1 MB
/// byte cap, character-level <c>max_chars</c> truncation. Defaults are tuned so
/// the model rarely has to think about them — a sensible <c>User-Agent</c> is
/// injected when absent (avoids GitHub/Reddit 403s), <c>max_chars</c> defaults
/// to 20000 (enough for most page summaries), and a clear truncation hint tells
/// the model when to narrow its URL instead of refetching with a larger window.
/// </remarks>
internal sealed class FetchTool : IAssistTool
{
    /// <summary>HTTP client reused across requests; per-request timeout is enforced via CancellationToken.</summary>
    private static readonly HttpClient SharedClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>Maximum response body size (1 MB) before the byte stream is cut off.</summary>
    private const int MaxResponseBytes = 1_048_576;

    /// <summary>Default character cap when the caller doesn't specify <c>max_chars</c>.</summary>
    private const int DefaultMaxChars = 20_000;

    /// <summary>Default User-Agent injected when the caller doesn't supply one.</summary>
    private const string DefaultUserAgent = "FieldCure-Sample-Fetch/1.0 (+https://github.com/FieldCure)";

    /// <summary>
    /// Headers the model is not allowed to set. Some are hop-by-hop or framing-critical
    /// (HttpClient owns them), and <c>Authorization</c> is blocked outright to keep
    /// prompt-injection attacks from exfiltrating credentials embedded in chat history
    /// to attacker-controlled URLs. Real auth flows should add a dedicated parameter
    /// the host controls — not flow auth through model-authored JSON.
    /// </summary>
    private static readonly HashSet<string> ForbiddenHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Transfer-Encoding",
        "Connection",
        "Authorization",
        "Cookie",
        "Proxy-Authorization",
    };

    /// <inheritdoc/>
    public string Name => "fetch";

    /// <inheritdoc/>
    public string Description =>
        "Fetch the contents of an http(s) URL. Returns JSON with statusCode, headers, " +
        "body, and elapsedMs. A sensible User-Agent is sent automatically. max_chars " +
        "defaults to 20000; if the body is truncated the response includes an explicit " +
        "notice — narrow the URL or raise max_chars rather than refetching the same page. " +
        "Do NOT use max_chars for JSON API responses where you need to parse the result.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "url": {
              "type": "string",
              "description": "Absolute http:// or https:// URL to fetch."
            },
            "max_chars": {
              "type": "integer",
              "description": "Maximum characters of the response body to return. Default 20000. Raise for long pages, or set to a small value when you only need a preview. Do not use for JSON APIs you intend to parse.",
              "minimum": 1
            },
            "timeout_seconds": {
              "type": "integer",
              "description": "Per-request timeout (default 30, max 120).",
              "minimum": 1,
              "maximum": 120
            },
            "headers": {
              "type": "object",
              "description": "Optional request headers. User-Agent is set automatically if not provided. Examples: {\"Accept\": \"application/json\"} for JSON APIs, {\"Accept\": \"application/vnd.github+json\"} for the GitHub REST API, {\"Accept-Language\": \"en-US,en;q=0.9\"} to prefer English content. Authorization, Cookie, and hop-by-hop headers are not accepted from the model.",
              "additionalProperties": { "type": "string" }
            }
          },
          "required": ["url"]
        }
        """;

    /// <summary>
    /// The sample auto-approves tool calls to keep the multi-turn loop simple. Set this
    /// to <c>true</c> to demonstrate <c>ToolApprovalPanel</c> — the consumer must then
    /// wire the panel's approval/decline events into its turn loop.
    /// </summary>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var url = parameters.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
        var maxChars = parameters.TryGetProperty("max_chars", out var mcEl) && mcEl.ValueKind == JsonValueKind.Number
            ? mcEl.GetInt32() : DefaultMaxChars;
        var timeoutSeconds = parameters.TryGetProperty("timeout_seconds", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number
            ? Math.Clamp(tsEl.GetInt32(), 1, 120) : 30;
        var requestedHeaders = ParseHeaders(parameters);

        try
        {
            var (uri, urlError) = SsrfGuard.ValidateUrl(url);
            if (uri is null)
                return SerializeError(urlError ?? "Invalid URL.");

            var ssrfError = await SsrfGuard.CheckAsync(uri, ct);
            if (ssrfError is not null)
                return SerializeError(ssrfError);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyHeaders(request, requestedHeaders);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            var sw = Stopwatch.StartNew();
            using var response = await SharedClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();

            var responseHeaders = new Dictionary<string, string>();
            foreach (var h in response.Headers.Concat(response.Content.Headers))
                responseHeaders[h.Key] = string.Join(", ", h.Value);

            var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            var buffer = new byte[MaxResponseBytes];
            var totalRead = 0;
            int read;
            while (totalRead < MaxResponseBytes &&
                   (read = await stream.ReadAsync(buffer.AsMemory(totalRead, MaxResponseBytes - totalRead), cts.Token)) > 0)
            {
                totalRead += read;
            }

            var bodyByteCap = totalRead >= MaxResponseBytes;
            var bodyText = Encoding.UTF8.GetString(buffer, 0, totalRead);

            // Character-level cap is applied after UTF-8 decoding so the model sees a
            // character count it can reason about — byte truncation alone can leave
            // half a multi-byte sequence at the tail. The explicit notice tells the
            // model to narrow the URL rather than ratchet max_chars up indefinitely.
            var charTruncated = false;
            if (maxChars > 0 && bodyText.Length > maxChars)
            {
                var limit = maxChars;
                if (limit > 0 && char.IsHighSurrogate(bodyText[limit - 1]))
                    limit--;

                var remaining = bodyText.Length - limit;
                bodyText = bodyText[..limit]
                    + $"\n\n[TRUNCATED: response was {bodyText.Length:N0} chars, "
                    + $"showing first {limit:N0}. If you need more, re-call with a larger "
                    + "max_chars OR (better) request a more specific URL. Do not refetch the "
                    + "same URL hoping for different content.]";
                charTruncated = true;
            }

            var result = new
            {
                statusCode = (int)response.StatusCode,
                headers = responseHeaders,
                body = bodyText,
                elapsedMs = sw.ElapsedMilliseconds,
                truncated = (bodyByteCap || charTruncated) ? (bool?)true : null,
                maxChars,
            };

            return JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (OperationCanceledException)
        {
            return SerializeError($"Request timed out after {timeoutSeconds}s.");
        }
        catch (Exception ex)
        {
            return SerializeError(ex.Message);
        }
    }

    /// <summary>
    /// Parses the optional <c>headers</c> argument into a case-insensitive dictionary,
    /// silently dropping non-string values. Returns an empty dictionary when the caller
    /// didn't pass a <c>headers</c> object.
    /// </summary>
    private static Dictionary<string, string> ParseHeaders(JsonElement parameters)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!parameters.TryGetProperty("headers", out var headersEl) || headersEl.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var prop in headersEl.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                dict[prop.Name] = prop.Value.GetString() ?? "";
        }
        return dict;
    }

    /// <summary>
    /// Applies the requested headers to <paramref name="request"/>, dropping forbidden
    /// names, and guarantees a default <c>User-Agent</c> when the caller didn't supply one.
    /// </summary>
    private static void ApplyHeaders(HttpRequestMessage request, Dictionary<string, string> requested)
    {
        if (!requested.ContainsKey("User-Agent"))
            requested["User-Agent"] = DefaultUserAgent;

        foreach (var (key, value) in requested)
        {
            if (ForbiddenHeaders.Contains(key)) continue;
            // TryAddWithoutValidation lets the model use less-common headers (e.g. Accept-Language)
            // without HttpClient rejecting them; framing/hop-by-hop headers are already filtered.
            request.Headers.TryAddWithoutValidation(key, value);
        }
    }

    /// <summary>JSON serializer options tuned to drop nulls so the model isn't fed noisy fields.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Builds a uniform <c>{"error": "..."}</c> payload for the failure branches.</summary>
    private static string SerializeError(string message)
        => JsonSerializer.Serialize(new { error = message }, JsonOpts);
}
