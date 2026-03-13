using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentView.AI.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;

namespace FluentView.AI.Rendering;

internal class WebViewChatRenderer
{
    private WebView2 _webView = null!;
    private TaskCompletionSource? _navigationTcs;

    public event EventHandler<string>? CopyRequested;
    public event EventHandler<string>? ContinueRequested;
    public event EventHandler<string>? MessageCopyRequested;
    public event EventHandler<string>? RetryRequested;
    public event EventHandler<(string MessageId, string NewText)>? EditRequested;
    public event EventHandler<string>? SummarizeRequested;

    public async Task InitializeAsync(WebView2 webView)
    {
        _webView = webView;

        await _webView.EnsureCoreWebView2Async();

        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

        var html = LoadEmbeddedResource("chat.html");

        _navigationTcs = new TaskCompletionSource();
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.NavigateToString(html);
        await _navigationTcs.Task;
        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;

        // Inject vendor JS libraries after page load
        await InjectVendorScriptsAsync();
    }

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

                marked.use({
                    extensions: [mathBlock, mathBlockBracket, mathInline, mathInlineParen]
                });

                marked.setOptions({
                    breaks: true,
                    gfm: true,
                    highlight: function(code, lang) {
                        if (lang && hljs.getLanguage(lang)) {
                            try { return hljs.highlight(code, { language: lang }).value; }
                            catch(e) {}
                        }
                        try { return hljs.highlightAuto(code).value; }
                        catch(e) {}
                        return '';
                    }
                });
            })();
            """;
        await _webView.ExecuteScriptAsync(configScript).AsTask();
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _navigationTcs?.TrySetResult();
    }

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
            else if (message?.StartsWith("debugCopy:") == true)
            {
                var html = message["debugCopy:".Length..];
                CopyToClipboard(html);
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private static void CopyToClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
    }

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

    private static string LoadEmbeddedResource(string endsWith)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public Task AppendUserMessageAsync(string id, string text, string timestamp,
        IReadOnlyList<ChatAttachment>? attachments = null)
    {
        var attachmentsJson = SerializeAttachments(attachments);
        var script = $"window.fluentChat.appendUserMessage({Js(id)}, {Js(text)}, {Js(timestamp)}, {attachmentsJson})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

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

    public Task BeginAssistantMessageAsync(string id, string? providerName = null, string? modelId = null)
    {
        var script = $"window.fluentChat.beginAssistantMessage({Js(id)}, {Js(providerName ?? "")}, {Js(modelId ?? "")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task ResumeMessageAsync(string id, string existingText)
    {
        var script = $"window.fluentChat.resumeMessage({Js(id)}, {Js(existingText)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task AppendTokenAsync(string id, string token)
    {
        var script = $"window.fluentChat.appendToken({Js(id)}, {Js(token)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task FinalizeMessageAsync(string id, string fullMarkdown, bool truncated = false, int tokenCount = 0)
    {
        var script = $"window.fluentChat.finalizeMessage({Js(id)}, {Js(fullMarkdown)}, {(truncated ? "true" : "false")}, {tokenCount})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task ScrollToBottomAsync()
    {
        return _webView.ExecuteScriptAsync("window.fluentChat.scrollToBottom()").AsTask();
    }

    public Task RemoveMessagesAfterAsync(string messageId)
    {
        var script = $"window.fluentChat.removeMessagesAfter({Js(messageId)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task SetLocaleStringsAsync(IDictionary<string, string> strings)
    {
        var json = JsonSerializer.Serialize(strings);
        var script = $"Object.assign(window._L, {json})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task SetThemeAsync(bool isDark)
    {
        var script = $"window.fluentChat.setTheme({(isDark ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task SetDebugModeAsync(bool enabled)
    {
        var script = $"window.fluentChat.setDebugMode({(enabled ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    private static string Js(string value) => JsonSerializer.Serialize(value);
}
