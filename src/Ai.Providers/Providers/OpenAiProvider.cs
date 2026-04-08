using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.Ai.Providers.Helpers;
using FieldCure.Ai.Providers.Models;

namespace FieldCure.Ai.Providers;

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

    #region Thinking Support

    /// <summary>
    /// Determines thinking support for an OpenAI model.
    /// O-series models (o1, o3, o4-mini, etc.) support extended thinking; GPT models do not.
    /// </summary>
    /// <param name="modelId">The model identifier to check.</param>
    /// <returns>The thinking support level for the model.</returns>
    public static ThinkingSupport GetThinkingSupportFor(string? modelId)
    {
        if (string.IsNullOrEmpty(modelId)) return ThinkingSupport.NotSupported;
        // o-series: starts with 'o' followed by a digit (o1, o3, o4-mini, etc.)
        if (modelId.Length >= 2 && modelId[0] is 'o' or 'O' && char.IsDigit(modelId[1]))
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

        var message = firstChoice.GetProperty("message");
        var content = message.TryGetProperty("content", out var contentEl) &&
                      contentEl.ValueKind != JsonValueKind.Null
            ? contentEl.GetString()
            : null;

        // Parse reasoning_details (MiniMax-style structured thinking)
        string? thinkingContent = null;
        if (message.TryGetProperty("reasoning_details", out var reasoningEl) &&
            reasoningEl.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var detail in reasoningEl.EnumerateArray())
            {
                if (detail.TryGetProperty("text", out var textEl) &&
                    textEl.ValueKind == JsonValueKind.String)
                    sb.Append(textEl.GetString());
            }
            if (sb.Length > 0)
                thinkingContent = sb.ToString();
        }

        // Strip <think>...</think> tags from content (fallback for providers not using reasoning_details)
        if (thinkingContent is null && content is not null)
            (content, thinkingContent) = StripThinkTags(content);

        // Parse tool calls from the response
        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsEl))
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                var function = tc.GetProperty("function");
                toolCalls.Add(new ToolCall
                {
                    Id = tc.GetProperty("id").GetString()!,
                    FunctionName = function.GetProperty("name").GetString()!,
                    Arguments = function.GetProperty("arguments").GetString()!
                });
            }
        }

        return new AiResponse
        {
            Content = content,
            ThinkingContent = thinkingContent,
            ToolCalls = toolCalls,
            Usage = tokenUsage,
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

        IsTruncated = false;
        var responseSb = new StringBuilder();
        var toolCallIds = new Dictionary<int, string>();
        var thinkTagParser = new ThinkTagParser();

        await foreach (var sse in SseReader.ReadEventsAsync(stream, ct))
        {
            if (sse.Data == "[DONE]")
            {
                LastRawResponse = responseSb.ToString();
                if (LastUsage is not null)
                    yield return new StreamEvent.Usage(LastUsage);
                yield return new StreamEvent.StreamCompleted(IsTruncated);
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

            // Parse reasoning_details (MiniMax-style structured thinking)
            if (delta.TryGetProperty("reasoning_details", out var reasoningEl) &&
                reasoningEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var detail in reasoningEl.EnumerateArray())
                {
                    if (detail.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        var text = textEl.GetString();
                        if (!string.IsNullOrEmpty(text))
                            yield return new StreamEvent.ThinkingDelta(text);
                    }
                }
            }

            if (delta.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    // Strip <think>...</think> tags from content, emitting ThinkingDelta for thinking text
                    foreach (var evt in thinkTagParser.Feed(text))
                        yield return evt;
                }
            }

            // Parse tool call chunks
            if (delta.TryGetProperty("tool_calls", out var toolCallsEl))
            {
                foreach (var tc in toolCallsEl.EnumerateArray())
                {
                    var idx = tc.GetProperty("index").GetInt32();

                    // First chunk for this index contains the id and function name
                    if (tc.TryGetProperty("id", out var idEl))
                    {
                        var id = idEl.GetString()!;
                        toolCallIds[idx] = id;
                        var funcName = tc.GetProperty("function").GetProperty("name").GetString()!;
                        yield return new StreamEvent.ToolCallStart(id, funcName);
                    }

                    // Subsequent chunks contain argument fragments
                    if (tc.TryGetProperty("function", out var funcEl) &&
                        funcEl.TryGetProperty("arguments", out var argsEl))
                    {
                        var argsChunk = argsEl.GetString();
                        if (!string.IsNullOrEmpty(argsChunk) && toolCallIds.TryGetValue(idx, out var toolId))
                            yield return new StreamEvent.ToolCallDelta(toolId, argsChunk);
                    }
                }
            }
        }

        LastRawResponse = responseSb.ToString();
        if (LastUsage is not null)
            yield return new StreamEvent.Usage(LastUsage);
        yield return new StreamEvent.StreamCompleted(IsTruncated);
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
        var systemPrompt = PromptBuilder.Build(request.SystemPrompt, request.WorkspaceText, request.ContextChunks, request.MemoryText);
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

            // Only process binary attachments (images, documents) for user messages.
            var imageAttachments = msg.Role == ChatRole.User
                ? msg.Attachments.Where(a => a.Type == AttachmentType.Image).ToList()
                : [];
            var textAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.TextFile)
                .ToList();
            var documentAttachments = msg.Role == ChatRole.User
                ? msg.Attachments.Where(a => a.Type == AttachmentType.Document).ToList()
                : [];

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
                    else if (_pdfCapability == PdfCapability.PageAsImage)
                    {
                        var pages = Helpers.AttachmentProcessor.RenderPdfPages(att.Data);
                        foreach (var page in pages)
                        {
                            var imgUrl = $"data:image/png;base64,{Convert.ToBase64String(page)}";
                            contentParts.Add(new JsonObject
                            {
                                ["type"] = "image_url",
                                ["image_url"] = new JsonObject
                                {
                                    ["url"] = imgUrl
                                }
                            });
                        }
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
            else if (msg.Role == ChatRole.Tool)
            {
                // Tool result message requires tool_call_id
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = msg.ToolCallId,
                    ["content"] = msg.Content
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
                            ["id"] = tc.Id,
                            ["type"] = "function",
                            ["function"] = new JsonObject
                            {
                                ["name"] = tc.FunctionName,
                                ["arguments"] = tc.Arguments
                            }
                        });
                    }
                    msgObj["tool_calls"] = toolCallsArr;
                }

                messages.Add(msgObj);
            }
        }

        var isOSeries = ModelId.StartsWith("o", StringComparison.OrdinalIgnoreCase);

        // o-series: max_completion_tokens covers BOTH reasoning + output tokens combined.
        // When thinking is enabled, sum output + thinking budget so reasoning doesn't starve output.
        // e.g. user sets output=4096, budget=16384 → max_completion_tokens=20480
        var maxTokens = request.MaxTokens;
        if (isOSeries && request.ThinkingEnabled)
        {
            var thinkingBudget = request.ThinkingBudget ?? 16384;
            maxTokens = request.MaxTokens + thinkingBudget;
        }

        var body = new JsonObject
        {
            ["model"] = ModelId,
            [isOSeries ? "max_completion_tokens" : "max_tokens"] = maxTokens,
            ["messages"] = messages,
            ["stream"] = stream
        };

        // o-series models do not support temperature parameter
        if (!isOSeries)
        {
            body["temperature"] = request.Temperature;
        }

        // Extended thinking: add reasoning_effort for o-series models
        if (request.ThinkingEnabled && isOSeries)
        {
            var effort = request.ThinkingBudget switch
            {
                null or <= 4096 => "low",
                <= 16384 => "medium",
                _ => "high"
            };
            body["reasoning_effort"] = effort;
        }

        // stream_options is only supported by OpenAI and Groq; custom providers may reject it
        if (stream && ProviderName is "OpenAI" or "Groq")
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

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

    /// <summary>
    /// Extracts and removes &lt;think&gt;...&lt;/think&gt; blocks from content,
    /// returning the cleaned content and the extracted thinking text.
    /// </summary>
    private static (string? content, string? thinking) StripThinkTags(string text)
    {
        const string open = "<think>";
        const string close = "</think>";

        var openIdx = text.IndexOf(open, StringComparison.Ordinal);
        if (openIdx < 0) return (text, null);

        var closeIdx = text.IndexOf(close, openIdx, StringComparison.Ordinal);
        if (closeIdx < 0) return (text, null);

        var thinking = text[(openIdx + open.Length)..closeIdx].Trim();
        var before = text[..openIdx];
        var after = text[(closeIdx + close.Length)..].TrimStart('\n', '\r');
        var content = (before + after).Trim();

        return (
            string.IsNullOrEmpty(content) ? null : content,
            string.IsNullOrEmpty(thinking) ? null : thinking
        );
    }

    #endregion

    #region IDisposable

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases managed resources when <paramref name="disposing"/> is <c>true</c>.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing && _ownsHttpClient)
            _httpClient.Dispose();
    }

    #endregion
}
