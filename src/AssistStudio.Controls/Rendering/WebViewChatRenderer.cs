using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace FieldCure.AssistStudio.Rendering;

/// <summary>
/// Renders chat messages inside a WebView2 control using an embedded HTML/JS chat UI.
/// Handles message lifecycle (append, stream tokens, finalize), theming, locale strings,
/// debug data, and WebView-to-host message routing.
/// </summary>
internal class WebViewChatRenderer
{
    #region Fields

    /// <summary>
    /// The WebView2 control used for rendering chat HTML.
    /// </summary>
    private WebView2 _webView = null!;

    /// <summary>
    /// Task completion source used to await the initial navigation completion.
    /// </summary>
    private TaskCompletionSource? _navigationTcs;

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the user copies code from a code block in the chat.
    /// </summary>
    public event EventHandler<string>? CopyRequested;

    /// <summary>
    /// Occurs when the user requests to continue a truncated assistant message.
    /// </summary>
    public event EventHandler<string>? ContinueRequested;

    /// <summary>
    /// Occurs when the user requests to copy the full content of a message.
    /// </summary>
    public event EventHandler<string>? MessageCopyRequested;

    /// <summary>
    /// Occurs when the user requests to retry (re-send) a user message.
    /// </summary>
    public event EventHandler<string>? RetryRequested;

    /// <summary>
    /// Occurs when the user edits a user message, providing the message ID and new text.
    /// </summary>
    public event EventHandler<(string MessageId, string NewText)>? EditRequested;

    /// <summary>
    /// Occurs when the user requests to summarize a specific message.
    /// </summary>
    public event EventHandler<string>? SummarizeRequested;

    /// <summary>
    /// Occurs when a keyboard shortcut is pressed inside the WebView2 that should be handled by the app.
    /// The string parameter is the shortcut name (e.g., "Ctrl+S", "Ctrl+Shift+S").
    /// </summary>
    public event EventHandler<string>? KeyboardShortcutPressed;

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes the WebView2 control by loading the embedded chat HTML and injecting vendor scripts.
    /// </summary>
    public async Task InitializeAsync(WebView2 webView)
    {
        _webView = webView;

        await _webView.EnsureCoreWebView2Async();

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Browser accelerator keys (Ctrl+S, Ctrl+P, etc.) are kept enabled so that
        // keydown events reach our JS listener, which forwards them via postMessage.
        // The JS handler calls preventDefault() to suppress browser-default behavior.

        var html = LoadEmbeddedResource("chat.html");

        _navigationTcs = new TaskCompletionSource();
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.NavigateToString(html);
        await _navigationTcs.Task;
        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

        // Inject vendor JS libraries after page load
        await InjectVendorScriptsAsync();

        // Forward Ctrl+key shortcuts from WebView2 to the host app via postMessage,
        // since WebView2 runs in a separate HWND and key events don't bubble to XAML.
        await _webView.ExecuteScriptAsync("""
            document.addEventListener('keydown', function(e) {
                if (e.ctrlKey && !e.altKey) {
                    const key = e.key.toLowerCase();
                    if (key === 's') {
                        e.preventDefault();
                        const shortcut = e.shiftKey ? 'Ctrl+Shift+S' : 'Ctrl+S';
                        window.chrome.webview.postMessage('shortcut:' + shortcut);
                    }
                }
            });
            """).AsTask();
    }

    /// <summary>
    /// Appends a user message bubble to the chat UI with optional attachments.
    /// </summary>
    public Task AppendUserMessageAsync(string id, string text, string timestamp,
        IReadOnlyList<ChatAttachment>? attachments = null)
    {
        var attachmentsJson = SerializeAttachments(attachments);
        var script = $"window.fluentChat.appendUserMessage({Js(id)}, {Js(text)}, {Js(timestamp)}, {attachmentsJson})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Begins a new assistant message bubble in the chat UI with provider and model information.
    /// </summary>
    public Task BeginAssistantMessageAsync(string id, string? providerName = null, string? modelId = null)
    {
        var script = $"window.fluentChat.beginAssistantMessage({Js(id)}, {Js(providerName ?? "")}, {Js(modelId ?? "")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Resumes streaming into an existing assistant message bubble after a continue operation.
    /// </summary>
    public Task ResumeMessageAsync(string id, string existingText)
    {
        var script = $"window.fluentChat.resumeMessage({Js(id)}, {Js(existingText)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends a single streaming token to an in-progress assistant message.
    /// </summary>
    public Task AppendTokenAsync(string id, string token)
    {
        var script = $"window.fluentChat.appendToken({Js(id)}, {Js(token)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Finalizes an assistant message with the full markdown content, truncation status, and token count.
    /// </summary>
    public Task FinalizeMessageAsync(string id, string fullMarkdown, bool truncated = false, int tokenCount = 0)
    {
        var script = $"window.fluentChat.finalizeMessage({Js(id)}, {Js(fullMarkdown)}, {(truncated ? "true" : "false")}, {tokenCount})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Scrolls the chat view to the bottom.
    /// </summary>
    public Task ScrollToBottomAsync()
    {
        return _webView.ExecuteScriptAsync("window.fluentChat.scrollToBottom()").AsTask();
    }

    /// <summary>
    /// Removes all message elements that appear after the specified message ID in the chat UI.
    /// </summary>
    public Task RemoveMessagesAfterAsync(string messageId)
    {
        var script = $"window.fluentChat.removeMessagesAfter({Js(messageId)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Sets localized UI strings (e.g., button labels) in the chat HTML.
    /// </summary>
    public Task SetLocaleStringsAsync(IDictionary<string, string> strings)
    {
        var json = JsonSerializer.Serialize(strings);
        var script = $"Object.assign(window._L, {json})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Applies a light or dark theme to the chat HTML.
    /// </summary>
    public Task SetThemeAsync(bool isDark)
    {
        var script = $"window.fluentChat.setTheme({(isDark ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Enables or disables debug mode, which shows "Copy Request" / "Copy Response" buttons.
    /// </summary>
    public Task SetDebugModeAsync(bool enabled)
    {
        var script = $"window.fluentChat.setDebugMode({(enabled ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Stores debug data (raw request body and response) for a user/assistant message pair.
    /// </summary>
    public Task SetDebugDataAsync(string userMsgId, string? requestBody, string assistantMsgId, string? rawResponse)
    {
        var script = $"window.fluentChat.setDebugData({Js(userMsgId)}, {Js(requestBody ?? "")}, {Js(assistantMsgId)}, {Js(rawResponse ?? "")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the WebView2 navigation completed event to signal initialization is ready.
    /// </summary>
    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _navigationTcs?.TrySetResult();
    }

    /// <summary>
    /// Handles incoming web messages from the chat HTML and routes them to the appropriate events.
    /// </summary>
    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var message = args.TryGetWebMessageAsString();
            if (message?.StartsWith("copyMsg:") == true)
            {
                var messageId = message["copyMsg:".Length..];
                MessageCopyRequested?.Invoke(this, messageId);
            }
            else if (message?.StartsWith("copy:") == true)
            {
                var codeText = message["copy:".Length..];
                CopyToClipboard(codeText);
                CopyRequested?.Invoke(this, codeText);
            }
            else if (message?.StartsWith("continue:") == true)
            {
                var messageId = message["continue:".Length..];
                ContinueRequested?.Invoke(this, messageId);
            }
            else if (message?.StartsWith("retry:") == true)
            {
                var messageId = message["retry:".Length..];
                RetryRequested?.Invoke(this, messageId);
            }
            else if (message?.StartsWith("edit:") == true)
            {
                var payload = message["edit:".Length..];
                var colonIdx = payload.IndexOf(':');
                if (colonIdx > 0)
                {
                    var messageId = payload[..colonIdx];
                    var newText = payload[(colonIdx + 1)..];
                    EditRequested?.Invoke(this, (messageId, newText));
                }
            }
            else if (message?.StartsWith("summarize:") == true)
            {
                var messageId = message["summarize:".Length..];
                SummarizeRequested?.Invoke(this, messageId);
            }
            else if (message?.StartsWith("shortcut:") == true)
            {
                var shortcut = message["shortcut:".Length..];
                KeyboardShortcutPressed?.Invoke(this, shortcut);
            }
            else if (message?.StartsWith("debugCopy:") == true)
            {
                var html = message["debugCopy:".Length..];
                CopyToClipboard(html);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Injects vendor JavaScript libraries (marked.js, highlight.js, KaTeX) into the WebView2.
    /// </summary>
    private async Task InjectVendorScriptsAsync()
    {
        // Inject marked.js
        var markedJs = LoadEmbeddedResource("marked.min.js");
        await _webView.ExecuteScriptAsync(markedJs).AsTask();

        // Inject highlight.js
        var highlightJs = LoadEmbeddedResource("highlight.min.js");
        await _webView.ExecuteScriptAsync(highlightJs).AsTask();

        // Inject KaTeX JS + CSS with embedded fonts
        var katexJs = LoadEmbeddedResource("katex.min.js");
        await _webView.ExecuteScriptAsync(katexJs).AsTask();
        await InjectKatexCssAsync();

        // Configure marked with highlight.js + KaTeX math extensions
        var configScript = """
            (function() {
                // Block-level math: $$...$$
                var mathBlock = {
                    name: 'mathBlock',
                    level: 'block',
                    start: function(src) {
                        return src.indexOf('$$');
                    },
                    tokenizer: function(src) {
                        var match = src.match(/^\$\$([\s\S]+?)\$\$/);
                        if (match) {
                            return { type: 'mathBlock', raw: match[0], text: match[1].trim() };
                        }
                    },
                    renderer: function(token) {
                        try {
                            return katex.renderToString(token.text, { displayMode: true, throwOnError: false });
                        } catch(e) { return token.raw; }
                    }
                };

                // Inline math: $...$
                var mathInline = {
                    name: 'mathInline',
                    level: 'inline',
                    start: function(src) {
                        return src.indexOf('$');
                    },
                    tokenizer: function(src) {
                        var match = src.match(/^\$([^\$\n]+?)\$/);
                        if (match) {
                            return { type: 'mathInline', raw: match[0], text: match[1].trim() };
                        }
                    },
                    renderer: function(token) {
                        try {
                            return katex.renderToString(token.text, { displayMode: false, throwOnError: false });
                        } catch(e) { return token.raw; }
                    }
                };

                // Block-level math: \[...\]
                var mathBlockBracket = {
                    name: 'mathBlockBracket',
                    level: 'block',
                    start: function(src) {
                        return src.indexOf('\\[');
                    },
                    tokenizer: function(src) {
                        var match = src.match(/^\\\[([\s\S]+?)\\\]/);
                        if (match) {
                            return { type: 'mathBlockBracket', raw: match[0], text: match[1].trim() };
                        }
                    },
                    renderer: function(token) {
                        try {
                            return katex.renderToString(token.text, { displayMode: true, throwOnError: false });
                        } catch(e) { return token.raw; }
                    }
                };

                // Inline math: \(...\)
                var mathInlineParen = {
                    name: 'mathInlineParen',
                    level: 'inline',
                    start: function(src) {
                        return src.indexOf('\\(');
                    },
                    tokenizer: function(src) {
                        var match = src.match(/^\\\(([^\)]+?)\\\)/);
                        if (match) {
                            return { type: 'mathInlineParen', raw: match[0], text: match[1].trim() };
                        }
                    },
                    renderer: function(token) {
                        try {
                            return katex.renderToString(token.text, { displayMode: false, throwOnError: false });
                        } catch(e) { return token.raw; }
                    }
                };

                // Custom renderer: syntax-highlight code blocks via hljs
                var renderer = new marked.Renderer();
                renderer.code = function(token) {
                    var code = token.text || '';
                    var lang = (token.lang || '').trim();
                    var highlighted;
                    if (lang && hljs.getLanguage(lang)) {
                        try { highlighted = hljs.highlight(code, { language: lang }).value; }
                        catch(e) { highlighted = null; }
                    }
                    if (!highlighted) {
                        try { highlighted = hljs.highlightAuto(code).value; }
                        catch(e) { highlighted = code; }
                    }
                    var escaped = lang ? lang.replace(/"/g, '&quot;') : '';
                    return '<pre><code class="hljs language-' + escaped + '">' + highlighted + '</code></pre>';
                };

                marked.use({
                    extensions: [mathBlock, mathBlockBracket, mathInline, mathInlineParen],
                    renderer: renderer,
                    breaks: true,
                    gfm: true
                });
            })();
            """;
        await _webView.ExecuteScriptAsync(configScript).AsTask();
    }

    /// <summary>
    /// Copies the given text to the system clipboard.
    /// </summary>
    private static void CopyToClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
    }

    /// <summary>
    /// Injects KaTeX CSS into the WebView2 with font URLs replaced by embedded base64 data URIs.
    /// </summary>
    private async Task InjectKatexCssAsync()
    {
        var css = LoadEmbeddedResource("katex.min.css");

        // Replace font url(fonts/KaTeX_XXX.woff2) with base64 data URIs
        css = Regex.Replace(css, @"url\(fonts/(KaTeX_[^)]+\.woff2)\)", match =>
        {
            var fileName = match.Groups[1].Value;
            var fontBytes = LoadEmbeddedBinaryResource(fileName);
            var base64 = Convert.ToBase64String(fontBytes);
            return $"url(data:font/woff2;base64,{base64})";
        });

        // Strip .woff and .ttf src alternatives (woff2 is sufficient for WebView2)
        css = Regex.Replace(css, @",url\(fonts/[^)]+\.woff\)\s*format\(""woff""\)", "");
        css = Regex.Replace(css, @",url\(fonts/[^)]+\.ttf\)\s*format\(""truetype""\)", "");

        var escapedCss = JsonSerializer.Serialize(css);
        var script = "(function() { var s = document.createElement('style'); s.textContent = " +
                     escapedCss + "; document.head.appendChild(s); })();";
        await _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Loads an embedded binary resource (e.g., font file) matching the given suffix from the executing assembly.
    /// </summary>
    private static byte[] LoadEmbeddedBinaryResource(string endsWith)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Loads an embedded text resource (e.g., HTML, JS, CSS) matching the given suffix from the executing assembly.
    /// </summary>
    private static string LoadEmbeddedResource(string endsWith)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Serializes a list of chat attachments to a JSON array string for use in JavaScript calls.
    /// </summary>
    private static string SerializeAttachments(IReadOnlyList<ChatAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
            return "null";

        var array = new JsonArray();
        foreach (var att in attachments)
        {
            var obj = new JsonObject
            {
                ["fileName"] = att.FileName,
                ["type"] = att.Type == AttachmentType.Image ? "image" : "file",
                ["mimeType"] = att.MimeType ?? "application/octet-stream"
            };

            if (att.Type == AttachmentType.Image)
            {
                obj["base64"] = Convert.ToBase64String(att.Data);
            }

            array.Add(obj);
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// JSON-escapes a string value for safe interpolation into JavaScript code.
    /// </summary>
    private static string Js(string value) => JsonSerializer.Serialize(value);

    #endregion
}
