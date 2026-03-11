using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentView.AI.Models;

namespace FluentView.AI.Providers;

public class ClaudeProvider : IAiProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly bool _ownsHttpClient;

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    public string ProviderName => "Claude";
    public string ModelId { get; }

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
        using var response = await SendRequestAsync(body, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: true);
        using var response = await SendRequestAsync(body, ct, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        await foreach (var sse in SseReader.ReadEventsAsync(stream, ct))
        {
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
            else if (sse.EventType == "message_stop")
            {
                yield break;
            }
        }
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

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
