using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentView.AI.Models;

namespace FluentView.AI.Providers;

public class OpenAiProvider : IAiProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly bool _ownsHttpClient;

    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public string ProviderName => "OpenAI";
    public string ModelId { get; }
    public TokenUsage? LastUsage { get; private set; }
    public bool IsTruncated { get; private set; }

    public OpenAiProvider(string apiKey, string model = "gpt-4o")
        : this(new HttpClient(), apiKey, model, ownsHttpClient: true)
    {
    }

    public OpenAiProvider(HttpClient httpClient, string apiKey, string model = "gpt-4o")
        : this(httpClient, apiKey, model, ownsHttpClient: false)
    {
    }

    private OpenAiProvider(HttpClient httpClient, string apiKey, string model, bool ownsHttpClient)
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

        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            LastUsage = new TokenUsage(
                usage.GetProperty("prompt_tokens").GetInt32(),
                usage.GetProperty("completion_tokens").GetInt32());
        }

        var firstChoice = doc.RootElement.GetProperty("choices")[0];
        IsTruncated = firstChoice.TryGetProperty("finish_reason", out var fr) &&
                      fr.GetString() == "length";

        return firstChoice
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: true);
        using var response = await SendRequestAsync(body, ct, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        IsTruncated = false;

        await foreach (var sse in SseReader.ReadEventsAsync(stream, ct))
        {
            if (sse.Data == "[DONE]")
                yield break;

            using var doc = JsonDocument.Parse(sse.Data);
            var root = doc.RootElement;

            // Parse usage from the final chunk (when stream_options.include_usage is true)
            if (root.TryGetProperty("usage", out var usage) &&
                usage.ValueKind == JsonValueKind.Object)
            {
                LastUsage = new TokenUsage(
                    usage.GetProperty("prompt_tokens").GetInt32(),
                    usage.GetProperty("completion_tokens").GetInt32());
            }

            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() == 0) continue;

            var choice = choices[0];

            // Check finish_reason for truncation
            if (choice.TryGetProperty("finish_reason", out var frEl) &&
                frEl.ValueKind == JsonValueKind.String)
            {
                IsTruncated = frEl.GetString() == "length";
            }

            var delta = choice.GetProperty("delta");
            if (delta.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                    yield return text;
            }
        }
    }

    private string BuildRequestBody(AiRequest request, bool stream)
    {
        var messages = new JsonArray();

        // OpenAI includes system messages in the messages array
        if (request.SystemPrompt is not null)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt
            });
        }

        foreach (var msg in request.Messages)
        {
            var role = msg.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.System => "system",
                _ => "user"
            };

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
                    var dataUrl = $"data:{att.MimeType ?? "image/png"};base64,{Convert.ToBase64String(att.Data)}";
                    contentParts.Add(new JsonObject
                    {
                        ["type"] = "image_url",
                        ["image_url"] = new JsonObject
                        {
                            ["url"] = dataUrl
                        }
                    });
                }

                // Append text file contents inline
                var textContent = msg.Content;
                foreach (var att in textAttachments)
                {
                    var fileText = Encoding.UTF8.GetString(att.Data);
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
                    var fileText = Encoding.UTF8.GetString(att.Data);
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

        if (stream)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
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
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(req, completionOption, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"OpenAI API error {(int)response.StatusCode}: {errorBody}",
                null,
                response.StatusCode);
        }

        return response;
    }

    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiModel>();
        foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString()!;
            var ownedBy = item.TryGetProperty("owned_by", out var ob) ? ob.GetString() : null;
            models.Add(new AiModel(id, null, ownedBy));
        }

        return models;
    }

    public async Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            req.Headers.Add("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(req, ct);
            if (response.IsSuccessStatusCode)
            {
                string? orgId = null;
                if (response.Headers.TryGetValues("openai-organization", out var orgValues))
                    orgId = orgValues.FirstOrDefault();

                return new ConnectionInfo(true, orgId, orgId, null);
            }

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
