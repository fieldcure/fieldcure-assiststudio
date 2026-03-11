using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

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

        var html = LoadEmbeddedHtml();

        _navigationTcs = new TaskCompletionSource();
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.NavigateToString(html);
        await _navigationTcs.Task;
        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
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
                CopyRequested?.Invoke(this, message["copy:".Length..]);
            }
        }
        catch
        {
            // Ignore malformed messages
        }
    }

    private static string LoadEmbeddedHtml()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith("chat.html", StringComparison.OrdinalIgnoreCase));

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public Task AppendUserMessageAsync(string id, string text, string timestamp)
    {
        var script = $"window.fluentChat.appendUserMessage({Js(id)}, {Js(text)}, {Js(timestamp)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
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
