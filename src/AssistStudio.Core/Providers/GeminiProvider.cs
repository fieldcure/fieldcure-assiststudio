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
    #region Fields

    /// <summary>The HTTP client used for API requests.</summary>
    private readonly HttpClient _httpClient;

    /// <summary>The Google AI API key for authentication.</summary>
    private readonly string _apiKey;

    /// <summary>Whether this instance owns (and should dispose) the HTTP client.</summary>
    private readonly bool _ownsHttpClient;

    #endregion

    #region Constants

    /// <summary>The base URL for the Google Generative Language API.</summary>
    private const string BaseUrl = "https://generativelanguage.googleapis.com";

    #endregion

    #region Properties

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

    /// <inheritdoc/>
    public PdfCapability PdfCapability => PdfCapability.NativePdf;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="GeminiProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public GeminiProvider(string apiKey, string model = "gemini-2.0-flash")
        : this(new HttpClient(), apiKey, model, ownsHttpClient: true)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="GeminiProvider"/> with an externally managed <see cref="HttpClient"/>.
    /// </summary>
    public GeminiProvider(HttpClient httpClient, string apiKey, string model = "gemini-2.0-flash")
        : this(httpClient, apiKey, model, ownsHttpClient: false)
    {
    }

    /// <summary>
    /// Internal constructor that captures all dependencies.
    /// </summary>
    private GeminiProvider(HttpClient httpClient, string apiKey, string model, bool ownsHttpClient)
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
        var body = BuildRequestBody(request);
        LastRequestBody = body;
        var url = $"{BaseUrl}/v1beta/models/{ModelId}:generateContent?key={_apiKey}";
        using var response = await SendRequestAsync(url, body, ct);

        var json = await response.Content.ReadAsStringAsync(ct);
        LastRawResponse = json;
        using var doc = JsonDocument.Parse(json);

        TokenUsage? tokenUsage = null;
        if (doc.RootElement.TryGetProperty("usageMetadata", out var usage))
        {
            var input = usage.TryGetProperty("promptTokenCount", out var pEl) ? pEl.GetInt32() : 0;
            var output = usage.TryGetProperty("candidatesTokenCount", out var cEl) ? cEl.GetInt32() : 0;
            tokenUsage = new TokenUsage(input, output);
            LastUsage = tokenUsage;
        }

        var firstCandidate = doc.RootElement.GetProperty("candidates")[0];
        IsTruncated = firstCandidate.TryGetProperty("finishReason", out var frEl) &&
                      frEl.GetString() == "MAX_TOKENS";

        // Parse parts — may contain text and/or functionCall
        string? textContent = null;
        var toolCalls = new List<ToolCall>();
        int fcIndex = 0;

        if (firstCandidate.TryGetProperty("content", out var contentEl) &&
            contentEl.TryGetProperty("parts", out var partsEl))
        {
            // Count functionCall parts to decide if suffix is needed
            int fcCount = 0;
            foreach (var p in partsEl.EnumerateArray())
                if (p.TryGetProperty("functionCall", out _)) fcCount++;

            foreach (var part in partsEl.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textEl))
                {
                    textContent = textEl.GetString();
                }
                else if (part.TryGetProperty("functionCall", out var fc))
                {
                    var funcName = fc.GetProperty("name").GetString()!;
                    toolCalls.Add(new ToolCall
                    {
                        // Gemini has no tool call IDs; use function name so
                        // ToolCallId in the result message carries the name
                        // needed by functionResponse. Add index suffix for
                        // uniqueness when multiple calls exist.
                        Id = fcCount > 1 ? $"{funcName}_{fcIndex}" : funcName,
                        FunctionName = funcName,
                        Arguments = fc.GetProperty("args").GetRawText()
                    });
                    fcIndex++;
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
    public async IAsyncEnumerable<StreamEvent> StreamAsync(AiRequest request, [EnumeratorCancellation] CancellationToken ct = default)
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
                    content.TryGetProperty("parts", out var parts))
                {
                    int fcIndex = 0;
                    // Count functionCall parts for ID suffix logic
                    int fcCount = 0;
                    foreach (var p in parts.EnumerateArray())
                        if (p.TryGetProperty("functionCall", out _)) fcCount++;

                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var textEl))
                        {
                            var text = textEl.GetString();
                            if (!string.IsNullOrEmpty(text))
                                yield return new StreamEvent.TextDelta(text);
                        }
                        else if (part.TryGetProperty("functionCall", out var fc))
                        {
                            var funcName = fc.GetProperty("name").GetString()!;
                            var id = fcCount > 1 ? $"{funcName}_{fcIndex}" : funcName;
                            var argsJson = fc.GetProperty("args").GetRawText();
                            yield return new StreamEvent.ToolCallStart(id, funcName);
                            yield return new StreamEvent.ToolCallDelta(id, argsJson);
                            fcIndex++;
                        }
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

    #endregion

    #region Private Methods

    /// <summary>Builds the JSON request body for the Gemini generateContent API.</summary>
    private string BuildRequestBody(AiRequest request)
    {
        var contents = new JsonArray();
        string? systemText = PromptBuilder.Build(request.SystemPrompt, request.WorkspaceText, request.ContextChunks);

        foreach (var msg in request.Messages)
        {
            if (msg.Role == ChatRole.System)
            {
                // Gemini uses systemInstruction, not in contents array
                systemText ??= msg.Content;
                continue;
            }

            // Tool result messages use functionResponse
            if (msg.Role == ChatRole.Tool)
            {
                JsonNode? responseNode;
                try { responseNode = JsonNode.Parse(msg.Content); }
                catch { responseNode = new JsonObject { ["result"] = msg.Content }; }

                // Strip index suffix (e.g. "scan_directory_0" → "scan_directory")
                var toolCallId = msg.ToolCallId ?? "unknown";
                var lastUnderscore = toolCallId.LastIndexOf('_');
                var funcName = lastUnderscore > 0 &&
                               int.TryParse(toolCallId[(lastUnderscore + 1)..], out _)
                    ? toolCallId[..lastUnderscore]
                    : toolCallId;

                contents.Add(new JsonObject
                {
                    ["role"] = "function",
                    ["parts"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["functionResponse"] = new JsonObject
                            {
                                ["name"] = funcName,
                                ["response"] = responseNode
                            }
                        }
                    }
                });
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
            var documentAttachments = msg.Attachments
                .Where(a => a.Type == AttachmentType.Document)
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

            // Add native PDF document parts (inlineData)
            foreach (var att in documentAttachments)
            {
                parts.Add(new JsonObject
                {
                    ["inlineData"] = new JsonObject
                    {
                        ["mimeType"] = att.MimeType ?? "application/pdf",
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

            // Assistant messages with tool calls need functionCall parts
            if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                foreach (var tc in msg.ToolCalls)
                {
                    parts.Add(new JsonObject
                    {
                        ["functionCall"] = new JsonObject
                        {
                            ["name"] = tc.FunctionName,
                            ["args"] = JsonNode.Parse(tc.Arguments)
                        }
                    });
                }
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

        // Extended thinking support
        if (request.ThinkingEnabled)
        {
            body["thinkingConfig"] = new JsonObject
            {
                ["thinkingBudget"] = request.ThinkingBudget ?? 4096
            };
        }

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

        // Add tool definitions when available
        if (request.Tools is { Count: > 0 })
        {
            var functionDeclarations = new JsonArray();
            foreach (var tool in request.Tools)
            {
                functionDeclarations.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParameterSchema)
                });
            }
            body["tools"] = new JsonArray
            {
                new JsonObject { ["functionDeclarations"] = functionDeclarations }
            };
        }

        return body.ToJsonString();
    }

    /// <summary>Sends an HTTP POST request to the Gemini API and validates the response.</summary>
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
