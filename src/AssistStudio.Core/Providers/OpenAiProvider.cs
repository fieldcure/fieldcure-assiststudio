using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// AI provider implementation for OpenAI-compatible APIs (OpenAI, Groq, and other compatible endpoints).
/// </summary>
public partial class OpenAiProvider : IAiProvider, IDisposable
{
    #region Fields

    /// <summary>The HTTP client used for API requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The API key for authentication.</summary>
    private readonly string _apiKey;

    /// <summary>The base URL of the OpenAI-compatible API endpoint.</summary>
    private readonly string _baseUrl;

    /// <summary>Whether this instance owns (and should dispose) the HTTP client.</summary>
    private readonly bool _ownsHttpClient;

    /// <summary>How this instance handles PDF document attachments.</summary>
    private readonly PdfCapability _pdfCapability;

    #endregion

    #region Properties

    /// <inheritdoc/>
    public string ProviderName { get; }

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

    /// <inheritdoc/>
    public PdfCapability PdfCapability => _pdfCapability;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="OpenAiProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public OpenAiProvider(string apiKey, string model = "gpt-4o",
        string baseUrl = "https://api.openai.com/v1", string providerName = "OpenAI",
        PdfCapability pdfCapability = PdfCapability.NativePdf)
        : this(new HttpClient(), apiKey, model, baseUrl, providerName, ownsHttpClient: true, pdfCapability)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="OpenAiProvider"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public OpenAiProvider(HttpClient httpClient, string apiKey, string model = "gpt-4o",
        string baseUrl = "https://api.openai.com/v1", string providerName = "OpenAI",
        PdfCapability pdfCapability = PdfCapability.NativePdf)
        : this(httpClient, apiKey, model, baseUrl, providerName, ownsHttpClient: false, pdfCapability)
    {
    }

    /// <summary>
    /// Internal constructor that captures all dependencies.
    /// </summary>
    private OpenAiProvider(HttpClient httpClient, string apiKey, string model,
        string baseUrl, string providerName, bool ownsHttpClient, PdfCapability pdfCapability)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _ownsHttpClient = ownsHttpClient;
        _pdfCapability = pdfCapability;
        ProviderName = providerName;
        ModelId = model;
    }

    #endregion

    #region IAiProvider Implementation

    /// <inheritdoc/>
    public async Task<AiResponse> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: false);
        LastRequestBody = body;
        using var response = await SendRequestAsync(body, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        LastRawResponse = json;
        using var doc = JsonDocument.Parse(json);

        TokenUsage? tokenUsage = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage))
        {
            tokenUsage = new TokenUsage(
                usage.GetProperty("prompt_tokens").GetInt32(),
                usage.GetProperty("completion_tokens").GetInt32());
            LastUsage = tokenUsage;
        }

        var firstChoice = doc.RootElement.GetProperty("choices")[0];
        IsTruncated = firstChoice.TryGetProperty("finish_reason", out var fr) &&
                      fr.GetString() == "length";

        var content = firstChoice
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";

        return new AiResponse
        {
            Content = content,
            Usage = tokenUsage,
            IsTruncated = IsTruncated
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: true);
        LastRequestBody = body;
        using var response = await SendRequestAsync(body, ct, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync(ct);

        IsTruncated = false;
        var responseSb = new StringBuilder();

        await foreach (var sse in SseReader.ReadEventsAsync(stream, ct))
        {
            if (sse.Data == "[DONE]")
            {
                LastRawResponse = responseSb.ToString();
                yield break;
            }

            responseSb.AppendLine(sse.Data);

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

        LastRawResponse = responseSb.ToString();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AiModel>> ListModelsAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
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

    /// <inheritdoc/>
    public async Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/models");
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

    #endregion

    #region Private Methods

    /// <summary>Builds the JSON request body for the OpenAI-compatible chat completions API.</summary>
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
                ChatRole.Tool => "tool",
                _ => "user"
            };

            var imageAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.Image)
                .ToList();
            var textAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.TextFile)
                .ToList();
            var documentAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.Document)
                .ToList();

            if (imageAttachments.Count > 0 || documentAttachments.Count > 0)
            {
                // Use multi-part content for messages with images or documents
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

                // Document attachments: native PDF or text extraction fallback
                var textContent = msg.Content;
                foreach (var att in documentAttachments)
                {
                    if (_pdfCapability == PdfCapability.NativePdf)
                    {
                        var dataUrl = $"data:{att.MimeType ?? "application/pdf"};base64,{Convert.ToBase64String(att.Data)}";
                        contentParts.Add(new JsonObject
                        {
                            ["type"] = "file",
                            ["file"] = new JsonObject
                            {
                                ["filename"] = att.FileName,
                                ["file_data"] = dataUrl
                            }
                        });
                    }
                    else
                    {
                        var pdfText = Helpers.AttachmentProcessor.ExtractTextFromPdf(att.Data);
                        textContent += $"\n\n[File: {att.FileName}]\n{pdfText}";
                    }
                }

                // Append text file contents inline
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

    /// <summary>Sends an HTTP POST request to the OpenAI-compatible API and validates the response.</summary>
    private async Task<HttpResponseMessage> SendRequestAsync(
        string body, CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(req, completionOption, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"{ProviderName} API error {(int)response.StatusCode}: {errorBody}",
                null,
                response.StatusCode);
        }

        return response;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    #endregion
}
