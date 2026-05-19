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
/// This implementation mirrors <c>FieldCure.Mcp.Essentials.HttpRequestTool</c>: SSRF
/// guard against private IPs, per-request timeout, hard 1 MB byte cap, and an optional
/// character-level <c>max_chars</c> truncation suitable for HTML responses.
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

    /// <inheritdoc/>
    public string Name => "fetch";

    /// <inheritdoc/>
    public string Description =>
        "Fetch the contents of an http(s) URL. Returns JSON with statusCode, headers, " +
        "body, and elapsedMs. Use max_chars (e.g., 3000-5000) for HTML pages when you " +
        "only need a portion. Do NOT use max_chars for JSON API responses — truncated " +
        "JSON cannot be parsed.";

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
              "description": "Maximum characters of the response body to return. Omit for unlimited (up to 1 MB).",
              "minimum": 1
            },
            "timeout_seconds": {
              "type": "integer",
              "description": "Per-request timeout (default 30, max 120).",
              "minimum": 1,
              "maximum": 120
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
            ? mcEl.GetInt32() : (int?)null;
        var timeoutSeconds = parameters.TryGetProperty("timeout_seconds", out var tsEl) && tsEl.ValueKind == JsonValueKind.Number
            ? Math.Clamp(tsEl.GetInt32(), 1, 120) : 30;

        try
        {
            var (uri, urlError) = SsrfGuard.ValidateUrl(url);
            if (uri is null)
                return SerializeError(urlError ?? "Invalid URL.");

            var ssrfError = await SsrfGuard.CheckAsync(uri, ct);
            if (ssrfError is not null)
                return SerializeError(ssrfError);

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
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

            var truncated = totalRead >= MaxResponseBytes;
            var bodyText = Encoding.UTF8.GetString(buffer, 0, totalRead);

            // Character-level cap is applied after UTF-8 decoding so the model sees a
            // character count it can reason about — byte truncation alone can leave
            // half a multi-byte sequence at the tail.
            if (maxChars is > 0 && bodyText.Length > maxChars.Value)
            {
                var limit = maxChars.Value;
                if (limit > 0 && char.IsHighSurrogate(bodyText[limit - 1]))
                    limit--;

                var remaining = bodyText.Length - limit;
                bodyText = bodyText[..limit]
                    + $"\n\n[Truncated: {remaining:N0} more chars omitted. "
                    + "Use a smaller max_chars or fetch a more specific URL.]";
                truncated = true;
            }

            var result = new
            {
                statusCode = (int)response.StatusCode,
                headers = responseHeaders,
                body = bodyText,
                elapsedMs = sw.ElapsedMilliseconds,
                truncated = truncated ? (bool?)true : null,
                maxChars = maxChars,
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

    /// <summary>JSON serializer options tuned to drop nulls so the model isn't fed noisy fields.</summary>
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Builds a uniform <c>{"error": "..."}</c> payload for the failure branches.</summary>
    private static string SerializeError(string message)
        => JsonSerializer.Serialize(new { error = message }, JsonOpts);
}
