using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// AI provider implementation for the Google Gemini API.
/// </summary>
public partial class GeminiProvider : IAiProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly bool _ownsHttpClient;

    private const string BaseUrl = "https://generativelanguage.googleapis.com";

    /// <inheritdoc/>
    public string ProviderName => "Gemini";

    /// <inheritdoc/>
    public string ModelId { get; }

    /// <inheritdoc/>
    public TokenUsage? LastUsage { get; private set; }

    /// <inheritdoc/>
    public bool IsTruncated { get; private set; }

    /// <inheritdoc/>
    public string? LastRequestBody { get; private set; }

    /// <inheritdoc/>
    public string? LastRawResponse { get; private set; }

    /// <summary>
    /// Initializes a new <see cref="GeminiProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="apiKey">The Google AI API key.</param>
    /// <param name="model">The model identifier to use.</param>
    public GeminiProvider(string apiKey, string model = "gemini-2.0-flash")
        : this(new HttpClient(), apiKey, model, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="GeminiProvider"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    /// <param name="apiKey">The Google AI API key.</param>
    /// <param name="model">The model identifier to use.</param>
    public GeminiProvider(HttpClient httpClient, string apiKey, string model = "gemini-2.0-flash")
        : this(httpClient, apiKey, model, ownsHttpClient: false)
    {
    }

    private GeminiProvider(HttpClient httpClient, string apiKey, string model, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _ownsHttpClient = ownsHttpClient;
        ModelId = model;
    }

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);
        LastRequestBody = body;
        var url = $"{BaseUrl}/v1beta/models/{ModelId}:generateContent?key={_apiKey}";
        using var response = await SendRequestAsync(url, body, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        LastRawResponse = json;
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
        {
            var input = usage.TryGetProperty("promptTokenCount", out var pEl) ? pEl.GetInt32() : 0;
            var output = usage.TryGetProperty("candidatesTokenCount", out var cEl) ? cEl.GetInt32() : 0;
            LastUsage = new TokenUsage(input, output);
        }

        var firstCandidate = doc.RootElement.GetProperty("candidates")[0];
        IsTruncated = firstCandidate.TryGetProperty("finishReason", out var frEl) &&
                      frEl.GetString() == "MAX_TOKENS";

        return firstCandidate
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString() ?? "";
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request);
        LastRequestBody = body;
        var url = $"{BaseUrl}/v1beta/models/{ModelId}:streamGenerateContent?key={_apiKey}&alt=sse";
        using var response = await SendRequestAsync(url, body, ct, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        IsTruncated = false;
        var responseSb = new StringBuilder();

        await foreach (var sse in SseReader.ReadEventsAsync(stream, ct))
        {
            if (string.IsNullOrEmpty(sse.Data)) continue;

            responseSb.AppendLine(sse.Data);

            using var doc = JsonDocument.Parse(sse.Data);
            var root = doc.RootElement;

            // Parse usage metadata (available in last chunk)
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                var input = usage.TryGetProperty("promptTokenCount", out var pEl) ? pEl.GetInt32() : 0;
                var output = usage.TryGetProperty("candidatesTokenCount", out var cEl) ? cEl.GetInt32() : 0;
                LastUsage = new TokenUsage(input, output);
            }

            if (root.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0)
            {
                var candidate = candidates[0];

                // Check finishReason for truncation
                if (candidate.TryGetProperty("finishReason", out var frEl))
                {
                    IsTruncated = frEl.GetString() == "MAX_TOKENS";
                }

                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.GetArrayLength() > 0)
                {
                    var part = parts[0];
                    if (part.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            yield return text;
                    }
                }
            }
        }

        LastRawResponse = responseSb.ToString();
    }

    private string BuildRequestBody(AiRequest request)
    {
        var contents = new JsonArray();
        string? systemText = request.SystemPrompt;

        foreach (var msg in request.Messages)
        {
            if (msg.Role == ChatRole.System)
            {
                // Gemini uses systemInstruction, not in contents array
                systemText ??= msg.Content;
                continue;
            }

            var role = msg.Role == ChatRole.User ? "user" : "model";
            var parts = new JsonArray();

            var imageAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.Image)
                .ToList();
            var textAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.TextFile)
                .ToList();

            // Add image parts (inlineData)
            foreach (var att in imageAttachments)
            {
                parts.Add(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = att.MimeType ?? "image/png",
                        ["data"] = Convert.ToBase64String(att.Data)
                    }
                });
            }

            // Build text content (with text file attachments appended)
            var textContent = msg.Content;
            foreach (var att in textAttachments)
            {
                var fileText = Encoding.UTF8.GetString(att.Data);
                textContent += $"\n\n[File: {att.FileName}]\n{fileText}";
            }

            if (!string.IsNullOrEmpty(textContent))
            {
                parts.Add(new JsonObject { ["text"] = textContent });
            }

            contents.Add(new JsonObject
            {
                ["role"] = role,
                ["parts"] = parts
            });
        }

        var body = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["maxOutputTokens"] = request.MaxTokens
            }
        };

        if (systemText is not null)
        {
            body["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = systemText }
                }
            };
        }

        return body.ToJsonString();
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        string url, string body, CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(req, completionOption, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Gemini API error {(int)response.StatusCode}: {errorBody}",
                null,
                response.StatusCode);
        }

        return response;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/v1beta/models?key={_apiKey}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiModel>();
        foreach (var item in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            // name is "models/gemini-2.0-flash" — strip prefix for Id
            var fullName = item.GetProperty("name").GetString()!;
            var id = fullName.StartsWith("models/") ? fullName["models/".Length..] : fullName;
            var displayName = item.TryGetProperty("displayName", out var dn) ? dn.GetString() : null;
            models.Add(new AiModel(id, displayName, "google"));
        }

        return models;
    }

    /// <inheritdoc/>
    public async Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{BaseUrl}/v1beta/models?key={_apiKey}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _httpClient.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
                return new ConnectionInfo(true, null, null, null);

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            return new ConnectionInfo(false, null, null, $"HTTP {(int)response.StatusCode}: {errorBody}");
        }
        catch (Exception ex)
        {
            return new ConnectionInfo(false, null, null, ex.Message);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
