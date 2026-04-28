using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FieldCure.Ai.Providers.Helpers;
using FieldCure.Ai.Providers.Models;
using static FieldCure.Ai.Providers.Helpers.OllamaErrorHelper;

namespace FieldCure.Ai.Providers;

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

    /// <summary>Duration to keep the model loaded in VRAM (Go duration format).</summary>
    private readonly string _keepAlive;

    /// <summary>Context window size in tokens.</summary>
    private readonly int _numCtx;

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

    /// <inheritdoc/>
    public AudioCapability AudioCapability => AudioCapability.NotSupported;

    /// <inheritdoc/>
    public ToolCallingSupport ToolCallingSupport => ToolCallingSupport.Supported;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new <see cref="OllamaProvider"/> with an internally managed <see cref="HttpClient"/>.
    /// </summary>
    public OllamaProvider(string model = "llama3.1", string baseUrl = "http://localhost:11434",
        PdfCapability pdfCapability = PdfCapability.TextExtraction,
        string keepAlive = "5m", int numCtx = 8192)
        : this(new HttpClient(), model, baseUrl, ownsHttpClient: true, pdfCapability, keepAlive, numCtx)
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
        PdfCapability pdfCapability, string keepAlive = "30m", int numCtx = 8192)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _keepAlive = keepAlive;
        _numCtx = numCtx;
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
            || modelId.StartsWith("qwq", StringComparison.OrdinalIgnoreCase)
            || modelId.Contains(":cloud", StringComparison.OrdinalIgnoreCase))
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
        var rawContent = message.TryGetProperty("content", out var contentEl)
            ? contentEl.GetString()
            : null;

        // 1) Ollama native thinking field (cloud models, etc.)
        string? thinkingContent = message.TryGetProperty("thinking", out var thinkEl)
            ? thinkEl.GetString() : null;

        // 2) Fallback: <think> tag parsing (deepseek-r1, qwq)
        var content = rawContent;
        if (thinkingContent is null && !string.IsNullOrEmpty(rawContent))
            (thinkingContent, content) = ExtractThinkingBlock(rawContent);

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
            ThinkingContent = thinkingContent,
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

        // Track <think>...</think> state across streamed chunks.
        // Models like deepseek-r1 and qwq emit reasoning inside these tags.
        var thinkState = new ThinkTagState();

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
                // Flush any remaining tag buffer as text (incomplete tag)
                if (thinkState.TagBuffer.Length > 0)
                {
                    yield return thinkState.InsideThink
                        ? new StreamEvent.ThinkingDelta(thinkState.TagBuffer.ToString())
                        : new StreamEvent.TextDelta(thinkState.TagBuffer.ToString());
                    thinkState.TagBuffer.Clear();
                }

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
                // Ollama native thinking field (cloud models, etc.)
                if (messageEl.TryGetProperty("thinking", out var thinkEl))
                {
                    var thinkText = thinkEl.GetString();
                    if (!string.IsNullOrEmpty(thinkText))
                        yield return new StreamEvent.ThinkingDelta(thinkText);
                }

                if (messageEl.TryGetProperty("content", out var contentEl))
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        foreach (var evt in ClassifyThinkingChunks(text, thinkState))
                            yield return evt;
                    }
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
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
        var response = await _httpClient.SendAsync(req, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/tags");
            var response = await _httpClient.SendAsync(req, timeoutCts.Token);

            if (response.IsSuccessStatusCode)
                return new ConnectionInfo(true, null, null, null);

            return new ConnectionInfo(false, null, null,
                OllamaErrorHelper.GetDefaultMessage(OllamaErrorCode.HttpError) + $" (HTTP {(int)response.StatusCode})");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ConnectionInfo(false, null, null,
                OllamaErrorHelper.GetDefaultMessage(OllamaErrorCode.Timeout));
        }
        catch (Exception ex)
        {
            var code = OllamaErrorHelper.Categorize(ex);
            return new ConnectionInfo(false, null, null,
                OllamaErrorHelper.GetDefaultMessage(code));
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Builds the JSON request body for the Ollama chat API.</summary>
    private string BuildRequestBody(AiRequest request, bool stream)
    {
        var messages = new JsonArray();

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

            // Labeled attachment layout
            var layout = msg.Role == ChatRole.User
                ? AttachmentLabelBuilder.Build(msg.Content, msg.Attachments)
                : null;

            string textContent;
            var imageDataList = new List<byte[]>();

            if (layout is not null)
            {
                // Ollama fallback: all labels go into text content
                var sb = new StringBuilder();

                // Binary labels first (describing what's in the images array)
                foreach (var seg in layout.BinarySegments)
                {
                    // Audio is unsupported on Ollama — silent skip per spec § 1.2 (history hygiene).
                    if (seg.Attachment.Type == AttachmentType.Audio) continue;

                    sb.AppendLine(seg.Label);

                    if (seg.Attachment.Type == AttachmentType.Image)
                    {
                        imageDataList.Add(seg.Attachment.Data);
                    }
                    else if (PdfCapability == PdfCapability.PageAsImage)
                    {
                        var pages = Helpers.AttachmentProcessor.RenderPdfPages(seg.Attachment.Data);
                        foreach (var page in pages)
                            imageDataList.Add(page);
                    }
                    // TextExtraction PDF: already inlined via layout.UserTextBlock
                }

                if (layout.BinarySegments.Count > 0)
                    sb.AppendLine();

                sb.Append(layout.UserTextBlock);
                textContent = sb.ToString();
            }
            else
            {
                textContent = msg.Content;
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
                            ["arguments"] = JsonNode.Parse(string.IsNullOrWhiteSpace(tc.Arguments) ? "{}" : tc.Arguments)
                        }
                    });
                }
                msgObj["tool_calls"] = toolCallsArr;
            }

            // Ollama uses "images" field for base64 image data
            if (imageDataList.Count > 0)
            {
                var images = new JsonArray();
                foreach (var data in imageDataList)
                    images.Add(Convert.ToBase64String(data));
                msgObj["images"] = images;
            }

            messages.Add(msgObj);
        }

        var body = new JsonObject
        {
            ["model"] = ModelId,
            ["messages"] = messages,
            ["stream"] = stream,
            ["keep_alive"] = _keepAlive,
            ["options"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["num_predict"] = request.MaxTokens,
                ["num_ctx"] = _numCtx
            }
        };

        // Enable thinking for models that support it (cloud models, etc.)
        if (request.ThinkingEnabled)
            body["think"] = true;

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

    /// <summary>
    /// Extracts a <c>&lt;think&gt;...&lt;/think&gt;</c> block from a complete response string.
    /// Returns (thinkingContent, remainingContent) where either may be null/empty.
    /// </summary>
    private static (string? thinking, string? content) ExtractThinkingBlock(string raw)
    {
        const string openTag = "<think>";
        const string closeTag = "</think>";

        var openIdx = raw.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (openIdx < 0) return (null, raw);

        var closeIdx = raw.IndexOf(closeTag, openIdx, StringComparison.OrdinalIgnoreCase);
        if (closeIdx < 0)
        {
            // Unterminated <think> — treat everything after the tag as thinking
            var thinking = raw[(openIdx + openTag.Length)..].Trim();
            var before = raw[..openIdx].Trim();
            return (
                string.IsNullOrEmpty(thinking) ? null : thinking,
                string.IsNullOrEmpty(before) ? null : before
            );
        }

        var thinkContent = raw[(openIdx + openTag.Length)..closeIdx].Trim();
        var remaining = string.Concat(raw.AsSpan(0, openIdx), raw.AsSpan(closeIdx + closeTag.Length)).Trim();
        return (
            string.IsNullOrEmpty(thinkContent) ? null : thinkContent,
            string.IsNullOrEmpty(remaining) ? null : remaining
        );
    }

    /// <summary>
    /// Classifies streamed text chunks into <see cref="StreamEvent.ThinkingDelta"/> or
    /// <see cref="StreamEvent.TextDelta"/> by tracking <c>&lt;think&gt;</c>/<c>&lt;/think&gt;</c> tags
    /// across chunk boundaries. Batches consecutive characters of the same type to avoid per-char yields.
    /// </summary>
    /// <param name="text">The incoming text chunk.</param>
    /// <param name="state">Mutable state tracking tag parsing across chunks.</param>
    private static IEnumerable<StreamEvent> ClassifyThinkingChunks(string text, ThinkTagState state)
    {
        var contentBatch = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '<')
            {
                // Flush content batch before starting tag detection
                if (contentBatch.Length > 0)
                {
                    yield return state.InsideThink
                        ? new StreamEvent.ThinkingDelta(contentBatch.ToString())
                        : new StreamEvent.TextDelta(contentBatch.ToString());
                    contentBatch.Clear();
                }

                // Flush any previous incomplete tag buffer as content
                if (state.TagBuffer.Length > 0)
                {
                    yield return state.InsideThink
                        ? new StreamEvent.ThinkingDelta(state.TagBuffer.ToString())
                        : new StreamEvent.TextDelta(state.TagBuffer.ToString());
                    state.TagBuffer.Clear();
                }
                state.TagBuffer.Append(ch);
            }
            else if (state.TagBuffer.Length > 0)
            {
                state.TagBuffer.Append(ch);
                var buf = state.TagBuffer.ToString();

                // Check for complete <think> tag
                if (buf.Equals("<think>", StringComparison.OrdinalIgnoreCase))
                {
                    state.InsideThink = true;
                    state.TagBuffer.Clear();
                }
                // Check for complete </think> tag
                else if (buf.Equals("</think>", StringComparison.OrdinalIgnoreCase))
                {
                    state.InsideThink = false;
                    state.TagBuffer.Clear();
                }
                // Still a valid prefix of <think> or </think>?
                else if (!"<think>".StartsWith(buf, StringComparison.OrdinalIgnoreCase)
                    && !"</think>".StartsWith(buf, StringComparison.OrdinalIgnoreCase))
                {
                    // Not a think tag — add to content batch
                    contentBatch.Append(buf);
                    state.TagBuffer.Clear();
                }
            }
            else
            {
                contentBatch.Append(ch);
            }
        }

        // Flush remaining content batch (tag buffer stays for cross-chunk continuity)
        if (contentBatch.Length > 0)
        {
            yield return state.InsideThink
                ? new StreamEvent.ThinkingDelta(contentBatch.ToString())
                : new StreamEvent.TextDelta(contentBatch.ToString());
        }
    }

    /// <summary>Mutable state for tracking <c>&lt;think&gt;</c> tag parsing across streamed chunks.</summary>
    private sealed class ThinkTagState
    {
        /// <summary>Whether the parser is currently inside a <c>&lt;think&gt;</c> block.</summary>
        public bool InsideThink;

        /// <summary>Accumulates characters that may form a complete opening or closing tag.</summary>
        public readonly StringBuilder TagBuffer = new();
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
