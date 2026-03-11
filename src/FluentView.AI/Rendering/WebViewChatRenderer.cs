using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
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

        // Configure marked with highlight.js integration
        var configScript = """
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
            if (message?.StartsWith("copy:") == true)
            {
                var codeText = message["copy:".Length..];
                CopyToClipboard(codeText);
                CopyRequested?.Invoke(this, codeText);
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

    public Task BeginAssistantMessageAsync(string id)
    {
        var script = $"window.fluentChat.beginAssistantMessage({Js(id)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task AppendTokenAsync(string id, string token)
    {
        var script = $"window.fluentChat.appendToken({Js(id)}, {Js(token)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task FinalizeMessageAsync(string id, string fullMarkdown)
    {
        var script = $"window.fluentChat.finalizeMessage({Js(id)}, {Js(fullMarkdown)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    public Task ScrollToBottomAsync()
    {
        return _webView.ExecuteScriptAsync("window.fluentChat.scrollToBottom()").AsTask();
    }

    public Task SetThemeAsync(bool isDark)
    {
        var script = $"window.fluentChat.setTheme({(isDark ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    private static string Js(string value) => JsonSerializer.Serialize(value);
}
