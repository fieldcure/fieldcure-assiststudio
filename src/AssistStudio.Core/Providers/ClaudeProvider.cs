using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Providers;

/// <summary>
/// AI provider implementation for the Anthropic Claude API.
/// </summary>
public partial class ClaudeProvider : IAiProvider, IDisposable
{
    #region Fields

    /// <summary>The HTTP client used for API requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The Anthropic API key for authentication.</summary>
    private readonly string _apiKey;

    /// <summary>Whether this instance owns (and should dispose) the HTTP client.</summary>
    private readonly bool _ownsHttpClient;

    #endregion

    #region Constants

    /// <summary>The Anthropic Messages API endpoint URL.</summary>
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    /// <summary>The Anthropic API version header value.</summary>
    private const string AnthropicVersion = "2023-06-01";

    #endregion

    #region Properties

    /// <inheritdoc/>
    public string ProviderName => "Claude";

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
    public PdfCapability PdfCapability => PdfCapability.NativePdf;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="ClaudeProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public ClaudeProvider(string apiKey, string model = "claude-sonnet-4-20250514")
        : this(new HttpClient(), apiKey, model, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="ClaudeProvider"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public ClaudeProvider(HttpClient httpClient, string apiKey, string model = "claude-sonnet-4-20250514")
        : this(httpClient, apiKey, model, ownsHttpClient: false)
    {
    }

    /// <summary>
    /// Internal constructor that captures all dependencies.
    /// </summary>
    private ClaudeProvider(HttpClient httpClient, string apiKey, string model, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _ownsHttpClient = ownsHttpClient;
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
                usage.GetProperty("input_tokens").GetInt32(),
                usage.GetProperty("output_tokens").GetInt32());
            LastUsage = tokenUsage;
        }

        IsTruncated = doc.RootElement.TryGetProperty("stop_reason", out var sr) &&
                      sr.GetString() == "max_tokens";

        // Parse content blocks — may contain text and/or tool_use
        string? textContent = null;
        var toolCalls = new List<ToolCall>();

        if (doc.RootElement.TryGetProperty("content", out var contentArr))
        {
            foreach (var block in contentArr.EnumerateArray())
            {
                var blockType = block.GetProperty("type").GetString();
                if (blockType == "text")
                {
                    textContent = block.GetProperty("text").GetString();
                }
                else if (blockType == "tool_use")
                {
                    toolCalls.Add(new ToolCall
                    {
                        Id = block.GetProperty("id").GetString()!,
                        FunctionName = block.GetProperty("name").GetString()!,
                        Arguments = block.GetProperty("input").GetRawText()
                    });
                }
            }
        }

        return new AiResponse
        {
            Content = textContent,
            ToolCalls = toolCalls,
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    #endregion

    #region Private Methods

    /// <summary>Builds the JSON request body for the Anthropic Messages API.</summary>
    private string BuildRequestBody(AiRequest request, bool stream)
    {
        var messages = new JsonArray();
        string? systemPrompt = PromptBuilder.Build(request.SystemPrompt, request.WorkspaceText, request.ContextChunks);

        foreach (var msg in request.Messages)
        {
            if (msg.Role == ChatRole.System)
            {
                // Claude uses a separate system parameter
                systemPrompt ??= msg.Content;
                continue;
            }

            // Tool result messages become user messages with tool_result content blocks.
            // Consecutive tool results are merged into a single user message
            // (Claude requires all tool_results for parallel calls in one message).
            if (msg.Role == ChatRole.Tool)
            {
                var toolResultBlock = new JsonObject
                {
                    ["type"] = "tool_result",
                    ["tool_use_id"] = msg.ToolCallId,
                    ["content"] = msg.Content
                };

                // Merge into previous user message if it contains tool_result blocks
                if (messages.Count > 0 &&
                    messages[^1] is JsonObject lastMsg &&
                    lastMsg["role"]?.GetValue<string>() == "user" &&
                    lastMsg["content"] is JsonArray contentArr &&
                    contentArr.Count > 0 &&
                    contentArr[0] is JsonObject firstBlock &&
                    firstBlock["type"]?.GetValue<string>() == "tool_result")
                {
                    contentArr.Add(toolResultBlock);
                }
                else
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "user",
                        ["content"] = new JsonArray { toolResultBlock }
                    });
                }
                continue;
            }

            var role = msg.Role == ChatRole.User ? "user" : "assistant";
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

                // Native PDF documents
                foreach (var att in documentAttachments)
                {
                    contentParts.Add(new JsonObject
                    {
                        ["type"] = "document",
                        ["source"] = new JsonObject
                        {
                            ["type"] = "base64",
                            ["media_type"] = att.MimeType ?? "application/pdf",
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

                // Assistant messages with tool calls need content blocks
                if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
                {
                    var contentBlocks = new JsonArray();
                    if (!string.IsNullOrEmpty(textContent))
                    {
                        contentBlocks.Add(new JsonObject
                        {
                            ["type"] = "text",
                            ["text"] = textContent
                        });
                    }
                    foreach (var tc in msg.ToolCalls)
                    {
                        contentBlocks.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.Id,
                            ["name"] = tc.FunctionName,
                            ["input"] = JsonNode.Parse(tc.Arguments)
                        });
                    }
                    messages.Add(new JsonObject
                    {
                        ["role"] = "assistant",
                        ["content"] = contentBlocks
                    });
                }
                else
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = role,
                        ["content"] = textContent
                    });
                }
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

        // Add tool definitions when available
        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.ParameterSchema)
                });
            }
            body["tools"] = tools;
        }

        return body.ToJsonString();
    }

    /// <summary>Sends an HTTP POST request to the Claude API and validates the response.</summary>
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
