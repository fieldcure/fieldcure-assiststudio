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

    /// <inheritdoc/>
    public PdfCapability PdfCapability { get; }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="OllamaProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaProvider(string model = "llama3.1", string baseUrl = "http://localhost:11434",
        PdfCapability pdfCapability = PdfCapability.TextExtraction)
        : this(new HttpClient(), model, baseUrl, ownsHttpClient: true, pdfCapability)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="OllamaProvider"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaProvider(HttpClient httpClient, string model = "llama3.1",
        PdfCapability pdfCapability = PdfCapability.TextExtraction)
        : this(httpClient, model, "http://localhost:11434", ownsHttpClient: false, pdfCapability)
    {
    }

    /// <summary>
    /// Internal constructor that captures all dependencies.
    /// </summary>
    private OllamaProvider(HttpClient httpClient, string model, string baseUrl, bool ownsHttpClient,
        PdfCapability pdfCapability)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        ModelId = model;
        PdfCapability = pdfCapability;
    }

    #endregion

    #region Thinking Support

    /// <summary>
    /// Determines thinking support for an Ollama model.
    /// Known thinking models (deepseek-r1, qwq) are supported; others are not by default.
    /// </summary>
    /// <param name="modelId">The model identifier to check.</param>
    /// <returns>The thinking support level for the model.</returns>
    public static ThinkingSupport GetThinkingSupportFor(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return ThinkingSupport.NotSupported;
        if (modelId.Contains("deepseek-r1", StringComparison.OrdinalIgnoreCase)
            || modelId.StartsWith("qwq", StringComparison.OrdinalIgnoreCase))
            return ThinkingSupport.Optional;
        return ThinkingSupport.NotSupported;
    }

    /// <inheritdoc/>
    public ThinkingSupport GetThinkingSupport(string modelId) => GetThinkingSupportFor(modelId);

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
        var root = doc.RootElement;

        TokenUsage? usage = null;
        if (root.TryGetProperty("prompt_eval_count", out var promptEl) &&
            root.TryGetProperty("eval_count", out var evalEl))
        {
            usage = new TokenUsage(promptEl.GetInt32(), evalEl.GetInt32());
            LastUsage = usage;
        }

        IsTruncated = root.TryGetProperty("done_reason", out var drEl) &&
                      drEl.GetString() == "length";

        var message = root.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString()
            : null;

        // Parse tool calls from the response
        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsEl))
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                var function = tc.GetProperty("function");
                toolCalls.Add(new ToolCall
                {
                    Id = Guid.NewGuid().ToString("N"),
                    FunctionName = function.GetProperty("name").GetString()!,
                    Arguments = function.GetProperty("arguments").GetRawText()
                });
            }
        }

        return new AiResponse
        {
            Content = content,
            ToolCalls = toolCalls,
            Usage = usage,
            IsTruncated = IsTruncated
        };
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
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
                if (LastUsage is not null)
                    yield return new StreamEvent.Usage(LastUsage);
                yield return new StreamEvent.StreamCompleted(IsTruncated);
                yield break;
            }

            if (root.TryGetProperty("message", out var messageEl))
            {
                if (messageEl.TryGetProperty("content", out var contentEl))
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                        yield return new StreamEvent.TextDelta(text);
                }

                // Parse tool calls (Ollama sends complete tool calls in a single chunk)
                if (messageEl.TryGetProperty("tool_calls", out var toolCallsEl))
                {
                    foreach (var tc in toolCallsEl.EnumerateArray())
                    {
                        var function = tc.GetProperty("function");
                        var id = Guid.NewGuid().ToString("N");
                        var funcName = function.GetProperty("name").GetString()!;
                        var argsJson = function.GetProperty("arguments").GetRawText();
                        yield return new StreamEvent.ToolCallStart(id, funcName);
                        yield return new StreamEvent.ToolCallDelta(id, argsJson);
                    }
                }
            }
        }

        LastRawResponse = responseSb.ToString();
        yield return new StreamEvent.StreamCompleted(IsTruncated);
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

        var systemPrompt = PromptBuilder.Build(request.SystemPrompt, request.WorkspaceText, request.ContextChunks);
        if (systemPrompt is not null)
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = systemPrompt
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

            // Append text file contents inline
            var textContent = msg.Content;
            foreach (var att in textAttachments)
            {
                var fileText = Encoding.UTF8.GetString(att.Data);
                textContent += $"\n\n[File: {att.FileName}]\n{fileText}";
            }

            // Document attachments: PageAsImage renders PDF pages into vision input
            foreach (var att in documentAttachments)
            {
                if (PdfCapability == PdfCapability.PageAsImage)
                {
                    var pages = Helpers.AttachmentProcessor.RenderPdfPages(att.Data);
                    foreach (var page in pages)
                        imageAttachments.Add(new ChatAttachment(att.FileName, AttachmentType.Image, page, "image/png"));
                }
                else
                {
                    var pdfText = Helpers.AttachmentProcessor.ExtractTextFromPdf(att.Data);
                    textContent += $"\n\n[File: {att.FileName}]\n{pdfText}";
                }
            }

            var msgObj = new JsonObject
            {
                ["role"] = role,
                ["content"] = textContent
            };

            // Include tool_calls on assistant messages that initiated tool calls
            if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                var toolCallsArr = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    toolCallsArr.Add(new JsonObject
                    {
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.FunctionName,
                            ["arguments"] = JsonNode.Parse(tc.Arguments)
                        }
                    });
                }
                msgObj["tool_calls"] = toolCallsArr;
            }

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

        // Add tool definitions when available
        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.ParameterSchema)
                    }
                });
            }
            body["tools"] = tools;
        }

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
