using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// AI provider implementation for the Ollama local inference server.
/// </summary>
public partial class OllamaProvider : IAiProvider, IDisposable
{
    #region Fields

    /// <summary>The HTTP client used for API requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The base URL of the Ollama server.</summary>
    private readonly string _baseUrl;

    /// <summary>Whether this instance owns (and should dispose) the HTTP client.</summary>
    private readonly bool _ownsHttpClient;

    #endregion

    #region Properties

    /// <inheritdoc/>
    public string ProviderName => "Ollama";

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

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="OllamaProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaProvider(string model = "llama3.1", string baseUrl = "http://localhost:11434")
        : this(new HttpClient(), model, baseUrl, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="OllamaProvider"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaProvider(HttpClient httpClient, string model = "llama3.1")
        : this(httpClient, model, "http://localhost:11434", ownsHttpClient: false)
    {
    }

    /// <summary>
    /// Internal constructor that captures all dependencies.
    /// </summary>
    private OllamaProvider(HttpClient httpClient, string model, string baseUrl, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        ModelId = model;
    }

    #endregion

    #region IAiProvider Implementation

    /// <inheritdoc/>
    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: false);
        LastRequestBody = body;
        using var response = await SendRequestAsync(body, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        LastRawResponse = json;
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("prompt_eval_count", out var promptEl) &&
            root.TryGetProperty("eval_count", out var evalEl))
        {
            LastUsage = new TokenUsage(promptEl.GetInt32(), evalEl.GetInt32());
        }

        IsTruncated = root.TryGetProperty("done_reason", out var drEl) &&
                      drEl.GetString() == "length";

        return root
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var body = BuildRequestBody(request, stream: true);
        LastRequestBody = body;
        using var response = await SendRequestAsync(body, ct, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        IsTruncated = false;
        var responseSb = new StringBuilder();

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;

            responseSb.AppendLine(line);

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (root.TryGetProperty("done", out var doneEl) && doneEl.GetBoolean())
            {
                if (root.TryGetProperty("prompt_eval_count", out var promptEl) &&
                    root.TryGetProperty("eval_count", out var evalEl))
                {
                    LastUsage = new TokenUsage(promptEl.GetInt32(), evalEl.GetInt32());
                }
                IsTruncated = root.TryGetProperty("done_reason", out var drEl) &&
                              drEl.GetString() == "length";
                LastRawResponse = responseSb.ToString();
                yield break;
            }

            if (root.TryGetProperty("message", out var messageEl) &&
                messageEl.TryGetProperty("content", out var contentEl))
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
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
        var response = await _httpClient.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var models = new List<AiModel>();
        foreach (var item in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            models.Add(new AiModel(name, name, "ollama"));
        }

        return models;
    }

    /// <inheritdoc/>
    public async Task<ConnectionInfo> ValidateConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
            var response = await _httpClient.SendAsync(req, ct);

            if (response.IsSuccessStatusCode)
                return new ConnectionInfo(true, null, null, null);

            return new ConnectionInfo(false, null, null, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return new ConnectionInfo(false, null, null, ex.Message);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Builds the JSON request body for the Ollama chat API.</summary>
    private string BuildRequestBody(AiRequest request, bool stream)
    {
        var messages = new JsonArray();

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

            // Append text file contents inline
            var textContent = msg.Content;
            foreach (var att in textAttachments)
            {
                var fileText = Encoding.UTF8.GetString(att.Data);
                textContent += $"\n\n[File: {att.FileName}]\n{fileText}";
            }

            var msgObj = new JsonObject
            {
                ["role"] = role,
                ["content"] = textContent
            };

            // Ollama uses "images" field for base64 image data
            if (imageAttachments.Count > 0)
            {
                var images = new JsonArray();
                foreach (var att in imageAttachments)
                {
                    images.Add(Convert.ToBase64String(att.Data));
                }
                msgObj["images"] = images;
            }

            messages.Add(msgObj);
        }

        var body = new JsonObject
        {
            ["model"] = ModelId,
            ["messages"] = messages,
            ["stream"] = stream,
            ["options"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxTokens
            }
        };

        return body.ToJsonString();
    }

    /// <summary>Sends an HTTP POST request to the Ollama API and validates the response.</summary>
    private async Task<HttpResponseMessage> SendRequestAsync(
        string body, CancellationToken ct,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(req, completionOption, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Ollama API error {(int)response.StatusCode}: {errorBody}",
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
