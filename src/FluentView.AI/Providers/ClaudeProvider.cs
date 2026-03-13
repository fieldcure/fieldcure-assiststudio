using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentView.AI.Models;

namespace FluentView.AI.Providers;

public partial class ClaudeProvider : IAiProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly bool _ownsHttpClient;

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public string ProviderName => "Claude";
    public string ModelId { get; }
    public TokenUsage? LastUsage { get; private set; }
    public bool IsTruncated { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastRawResponse { get; private set; }

    public ClaudeProvider(string apiKey, string model = "claude-sonnet-4-20250514")
        : this(new HttpClient(), apiKey, model, ownsHttpClient: true)
    {
    }

    public ClaudeProvider(HttpClient httpClient, string apiKey, string model = "claude-sonnet-4-20250514")
        : this(httpClient, apiKey, model, ownsHttpClient: false)
    {
    }

    private ClaudeProvider(HttpClient httpClient, string apiKey, string model, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _ownsHttpClient = ownsHttpClient;
        ModelId = model;
    }

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: false);
        LastRequestBody = body;
        using var response = await SendRequestAsync(body, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        LastRawResponse = json;
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            LastUsage = new TokenUsage(
                usage.GetProperty("input_tokens").GetInt32(),
                usage.GetProperty("output_tokens").GetInt32());
        }

        IsTruncated = doc.RootElement.TryGetProperty("stop_reason", out var sr) &&
                      sr.GetString() == "max_tokens";

        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: true);
        LastRequestBody = body;
        using var response = await SendRequestAsync(body, ct, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        int inputTokens = 0, outputTokens = 0;
        IsTruncated = false;
        var responseSb = new StringBuilder();

        await foreach (var sse in SseReader.ReadEventsAsync(stream, ct))
        {
            responseSb.AppendLine($"event: {sse.EventType}");
            responseSb.AppendLine($"data: {sse.Data}");
            responseSb.AppendLine();
            if (sse.EventType == "content_block_delta")
            {
                using var doc = JsonDocument.Parse(sse.Data);
                var text = doc.RootElement
                    .GetProperty("delta")
                    .GetProperty("text")
                    .GetString();

                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
            else if (sse.EventType == "message_start")
            {
                using var doc = JsonDocument.Parse(sse.Data);
                if (doc.RootElement.TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("usage", out var usage))
                {
                    inputTokens = usage.GetProperty("input_tokens").GetInt32();
                }
            }
            else if (sse.EventType == "message_delta")
            {
                using var doc = JsonDocument.Parse(sse.Data);
                if (doc.RootElement.TryGetProperty("usage", out var usage) &&
                    usage.TryGetProperty("output_tokens", out var outEl))
                {
                    outputTokens = outEl.GetInt32();
                }
                if (doc.RootElement.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("stop_reason", out var stopEl))
                {
                    IsTruncated = stopEl.GetString() == "max_tokens";
                }
            }
            else if (sse.EventType == "message_stop")
            {
                LastUsage = new TokenUsage(inputTokens, outputTokens);
                LastRawResponse = responseSb.ToString();
                yield break;
            }
        }

        LastUsage = new TokenUsage(inputTokens, outputTokens);
        LastRawResponse = responseSb.ToString();
    }

    private string BuildRequestBody(AiRequest request, bool stream)
    {
        var messages = new JsonArray();
        string? systemPrompt = request.SystemPrompt;

        foreach (var msg in request.Messages)
        {
            if (msg.Role == ChatRole.System)
            {
                // Claude uses a separate system parameter
                systemPrompt ??= msg.Content;
                continue;
            }

            var role = msg.Role == ChatRole.User ? "user" : "assistant";
            var imageAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.Image)
                .ToList();
            var textAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.TextFile)
                .ToList();

            if (imageAttachments.Count > 0)
            {
                // Use multi-part content for messages with images
                var contentParts = new JsonArray();

                foreach (var att in imageAttachments)
                {
                    contentParts.Add(new JsonObject
                    {
                        ["type"] = "image",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = att.MimeType ?? "image/png",
                            ["data"] = Convert.ToBase64String(att.Data)
                        }
                    });
                }

                // Append text file contents inline
                var textContent = msg.Content;
                foreach (var att in textAttachments)
                {
                    var fileText = System.Text.Encoding.UTF8.GetString(att.Data);
                    textContent += $"\n\n[File: {att.FileName}]\n{fileText}";
                }

                if (!string.IsNullOrEmpty(textContent))
                {
                    contentParts.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = textContent
                    });
                }

                messages.Add(new JsonObject
                {
                    ["role"] = role,
                    ["content"] = contentParts
                });
            }
            else
            {
                // Text-only message (possibly with text file attachments)
                var textContent = msg.Content;
                foreach (var att in textAttachments)
                {
                    var fileText = System.Text.Encoding.UTF8.GetString(att.Data);
                    textContent += $"\n\n[File: {att.FileName}]\n{fileText}";
                }

                messages.Add(new JsonObject
                {
                    ["role"] = role,
                    ["content"] = textContent
                });
            }
        }

        var body = new JsonObject
        {
            ["model"] = ModelId,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["messages"] = messages,
            ["stream"] = stream
        };

        if (systemPrompt is not null)
        {
            body["system"] = systemPrompt;
        }

        return body.ToJsonString();
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        string body, CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        var response = await _httpClient.SendAsync(req, completionOption, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Claude API error {(int)response.StatusCode}: {errorBody}",
                null,
                response.StatusCode);
        }

        return response;
    }

    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        var response = await _httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiModel>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var displayName = item.TryGetProperty("display_name", out var dn) ? dn.GetString() : null;
            models.Add(new AiModel(id, displayName, "anthropic"));
        }

        return models;
    }

    public async Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
            req.Headers.Add("x-api-key", _apiKey);
            req.Headers.Add("anthropic-version", AnthropicVersion);

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

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
