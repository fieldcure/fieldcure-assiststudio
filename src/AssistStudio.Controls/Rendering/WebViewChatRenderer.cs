using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using FieldCure.AssistStudio.Core.Helpers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

namespace FieldCure.AssistStudio.Controls.Rendering;

/// <summary>
/// Renders chat messages inside a WebView2 control using an embedded HTML/JS chat UI.
/// Handles message lifecycle (append, stream tokens, finalize), theming, locale strings,
/// debug data, and WebView-to-host message routing.
/// </summary>
internal partial class WebViewChatRenderer
{
    #region Constants

    /// <summary>
    /// Root directory for temporary media files (large audio/video from tool results).
    /// </summary>
    internal static readonly string TempRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "AssistStudio", "temp");

    #endregion

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

    #region Properties

    /// <summary>
    /// Gets or sets the workspace folder paths for the current conversation.
    /// Used by <see cref="IsAllowedFilePath"/> to permit file:// navigation within workspace directories.
    /// </summary>
    internal IReadOnlyList<string>? WorkspaceFolders { get; set; }

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
    /// Occurs when the user requests to edit a user message. Payload is the message ID.
    /// The host should populate the compose bar with the message content and enter edit mode;
    /// the actual text/attachment changes are read from the compose bar at confirm time.
    /// </summary>
    public event EventHandler<string>? EditRequested;

    /// <summary>
    /// Occurs when a keyboard shortcut is pressed inside the WebView2 that should be handled by the app.
    /// The string parameter is the shortcut name (e.g., "Ctrl+S", "Ctrl+Shift+S").
    /// </summary>
    public event EventHandler<string>? KeyboardShortcutPressed;

    /// <summary>
    /// Occurs when the user clicks a branch navigation arrow.
    /// Payload: (MessageId, Direction: -1 for previous, +1 for next).
    /// </summary>
    public event EventHandler<(string MessageId, int Direction)>? BranchSwitchRequested;

    /// <summary>
    /// Occurs when the user requests to save an image (data URI or URL).
    /// </summary>
    public event EventHandler<string>? ImageSaveRequested;

    /// <summary>
    /// Occurs when the user requests to copy an image (data URI or URL).
    /// </summary>
    public event EventHandler<string>? ImageCopyRequested;

    /// <summary>
    /// Occurs when the user requests to save a Mermaid/SVG diagram as an .svg file.
    /// Payload is the raw SVG markup (already serialized by the WebView).
    /// </summary>
    public event EventHandler<string>? DiagramSvgSaveRequested;

    /// <summary>
    /// Occurs when the user requests to copy raw SVG markup to the clipboard.
    /// </summary>
    public event EventHandler<string>? DiagramSvgCopyRequested;

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
        _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
        _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        _webView.CoreWebView2.DownloadStarting += OnDownloadStarting;
        _webView.CoreWebView2.ProcessFailed += OnProcessFailed;

        // Restrict outbound network access to the artifact-preview CDN allow-list.
        // The filter must be registered BEFORE any iframe runs, so do it here at
        // initialization. See WebViewChatRenderer.ArtifactPreview.cs/AllowedCdnRoutes
        // for the trusted hosts; everything else gets a 403.
        _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        _webView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
        _webView.CoreWebView2.IsDefaultDownloadDialogOpenChanged += (_, _) =>
        {
            if (_webView.CoreWebView2.IsDefaultDownloadDialogOpen)
                _webView.CoreWebView2.CloseDefaultDownloadDialog();
        };

        // Enable browser accelerator keys so clipboard shortcuts (Ctrl+C/V/X/A) work natively.
        // Unwanted browser shortcuts (Ctrl+F, Ctrl+P, etc.) are blocked via JS keydown handler.
        _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

        // Map temp media folder to a virtual host so <audio>/<video> can load file:// URIs.
        // Without this, WebView2 blocks local file access from data: origin pages.
        Directory.CreateDirectory(TempRoot);
        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "assiststudio.temp", TempRoot,
            CoreWebView2HostResourceAccessKind.Allow);

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
                    if (key === 'f') {
                        e.preventDefault();
                        window.chrome.webview.postMessage('shortcut:Ctrl+F');
                    }
                }
            });
            """).AsTask();
    }

    /// <summary>
    /// Appends a user message bubble to the chat UI with optional attachments.
    /// </summary>
    public Task AppendUserMessageAsync(string id, string text, string timestamp,
        IReadOnlyList<ChatAttachment>? attachments = null,
        int siblingIndex = 0, int siblingCount = 1)
    {
        var attachmentsJson = SerializeAttachments(attachments);
        var script = $"window.assistChat.appendUserMessage({Js(id)}, {Js(text)}, {Js(timestamp)}, {attachmentsJson}, {siblingIndex}, {siblingCount})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Begins a new assistant message bubble in the chat UI with provider and model information.
    /// When <paramref name="isContinuation"/> is true, the bubble is prefixed with a small
    /// "↪ continued" label so the reader can see it picks up from the previous (truncated)
    /// assistant message rather than answering a fresh prompt — the visual cue that hides
    /// the "Continue writing…" user turn behind the scenes.
    /// </summary>
    public Task BeginAssistantMessageAsync(string id, string? providerName = null, string? modelId = null,
        bool isSummary = false, bool isContinuation = false)
    {
        var script = $"window.assistChat.beginAssistantMessage({Js(id)}, {Js(providerName ?? "")}, {Js(modelId ?? "")}, {(isSummary ? "true" : "false")}, {(isContinuation ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Resumes streaming into an existing assistant message bubble after a continue operation.
    /// </summary>
    public Task ResumeMessageAsync(string id, string existingText)
    {
        var script = $"window.assistChat.resumeMessage({Js(id)}, {Js(existingText)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends a single streaming token to an in-progress assistant message.
    /// </summary>
    public Task AppendTokenAsync(string id, string token)
    {
        var script = $"window.assistChat.appendToken({Js(id)}, {Js(token)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Begins a collapsible thinking block inside an assistant message.
    /// </summary>
    public Task BeginThinkingBlockAsync(string id)
    {
        var script = $"window.assistChat.beginThinkingBlock({Js(id)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends a token to the thinking block of an assistant message.
    /// </summary>
    public Task AppendThinkingTokenAsync(string id, string token)
    {
        var script = $"window.assistChat.appendThinkingToken({Js(id)}, {Js(token)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Ends the thinking block and collapses it by default.
    /// </summary>
    public Task EndThinkingBlockAsync(string id)
    {
        var script = $"window.assistChat.endThinkingBlock({Js(id)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends a tool call block with optional details to an assistant message.
    /// </summary>
    public Task AppendToolBlockAsync(
        string id,
        string toolName,
        string? arguments = null,
        string? result = null,
        long? durationMs = null,
        bool isError = false)
    {
        var info = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = toolName,
            ["args"] = arguments,
            ["result"] = result is { Length: > 500 } ? result[..500] + "…" : result,
            ["ms"] = durationMs,
            ["error"] = isError
        };
        var script = $"window.assistChat.appendToolBlock({Js(id)}, {info.ToJsonString(WebViewJsonOptions)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Renders a pending tool block (pulsing header, no result yet) on an
    /// assistant message and tags it with <paramref name="callId"/> so
    /// <see cref="ResolveToolBlockAsync"/> can later fill it in. Use this at
    /// the moment a tool starts executing to give the user immediate feedback,
    /// especially for long-running calls where the result would otherwise
    /// arrive seconds or minutes later. The wrapper is generic (not
    /// sub-agent specific) so any tool can adopt the pending/resolve pattern.
    /// </summary>
    /// <param name="id">Assistant message id.</param>
    /// <param name="callId">Unique tool-call id (typically provider-issued).</param>
    /// <param name="toolName">Tool name shown in the header.</param>
    /// <param name="arguments">Optional arguments JSON shown in the preview.</param>
    public Task BeginToolBlockAsync(
        string id,
        string callId,
        string toolName,
        string? arguments = null)
    {
        var info = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = toolName,
            ["args"] = arguments,
        };
        var script = $"window.assistChat.beginToolBlock({Js(id)}, {Js(callId)}, {info.ToJsonString(WebViewJsonOptions)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Fills a previously-rendered pending tool block (created via
    /// <see cref="BeginToolBlockAsync"/>) with its result, stopping the
    /// pulse animation and rewriting the block as a collapsible details
    /// section in the same DOM position. If the pending block is not
    /// found (e.g., the message was re-rendered from saved state), the
    /// JS side falls back to <c>appendToolBlock</c> so the result still
    /// surfaces — the caller does not need to branch on that case.
    /// </summary>
    /// <param name="id">Assistant message id.</param>
    /// <param name="callId">Tool-call id used in <see cref="BeginToolBlockAsync"/>.</param>
    /// <param name="toolName">Tool name shown in the header.</param>
    /// <param name="arguments">Optional arguments JSON shown in Arguments section.</param>
    /// <param name="result">Tool result text (truncated to 500 chars for preview, same policy as <see cref="AppendToolBlockAsync"/>).</param>
    /// <param name="durationMs">Optional elapsed milliseconds shown in header.</param>
    /// <param name="isError">Whether the tool failed.</param>
    public Task ResolveToolBlockAsync(
        string id,
        string callId,
        string toolName,
        string? arguments = null,
        string? result = null,
        long? durationMs = null,
        bool isError = false)
    {
        var info = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = toolName,
            ["args"] = arguments,
            ["result"] = result is { Length: > 500 } ? result[..500] + "…" : result,
            ["ms"] = durationMs,
            ["error"] = isError,
        };
        var script = $"window.assistChat.resolveToolBlock({Js(id)}, {Js(callId)}, {info.ToJsonString(WebViewJsonOptions)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends a markdown-rendered text segment to the assistant message bubble.
    /// Used during restore to interleave text between tool blocks.
    /// </summary>
    public Task AppendRenderedSegmentAsync(string id, string markdownText)
    {
        var script = $"window.assistChat.appendRenderedSegment({Js(id)}, {Js(markdownText)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Pauses all playing audio and video elements in the chat.
    /// Called when the tab is switched or hidden.
    /// </summary>
    public Task PauseAllMediaAsync()
    {
        var script = "document.querySelectorAll('audio, video').forEach(function(el) { el.pause(); })";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends media content to an assistant message, rendered below the most recent tool block.
    /// Dispatches to the appropriate JS renderer based on the media kind (image, audio, video, download).
    /// </summary>
    public Task AppendToolMediaAsync(string id, MediaContent media)
    {
        var kindStr = media.Kind switch
        {
            MediaContentKind.Image => "image",
            MediaContentKind.Audio => "audio",
            MediaContentKind.Video => "video",
            _ => "download"
        };
        var script = $"window.assistChat.appendToolMedia({Js(id)}, {Js(media.MediaUri)}, {Js(kindStr)}, {Js(media.MimeType)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Appends a collapsible search result block (details/summary) to an assistant message.
    /// Used for <c>search_documents</c> tool results from RAG.
    /// </summary>
    public Task AppendSearchResultBlockAsync(string id, string searchResultJson, string displayName)
    {
        var script = $"window.assistChat.appendSearchResultBlock({Js(id)}, {Js(searchResultJson)}, {Js(displayName)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Marks the message with <c>data-edit-target</c> (dimmed via CSS) and hides
    /// all subsequent message bubbles. Used when entering edit mode.
    /// </summary>
    public Task BeginEditAsync(string messageId)
    {
        var script = $"window.assistChat.beginEdit({Js(messageId)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Removes the <c>data-edit-target</c> marker and restores all hidden bubbles.
    /// Must be called on both cancel AND confirm.
    /// </summary>
    public Task EndEditAsync()
    {
        return _webView.ExecuteScriptAsync("window.assistChat.endEdit()").AsTask();
    }

    /// <summary>
    /// Toggles a body-level <c>data-streaming</c> flag. The chat HTML uses this to
    /// disable Edit/Retry buttons while a response is streaming.
    /// </summary>
    public Task SetStreamingAsync(bool isStreaming)
    {
        var flag = isStreaming ? "true" : "false";
        var script = $"document.body.setAttribute('data-streaming', '{flag}')";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Finalizes an assistant message with the full markdown content, truncation status, and token count.
    /// <para/>
    /// <paramref name="truncated"/> drives the live Continue button. It is only set during a
    /// real (live) stream; restored conversations always pass <c>false</c> so a phantom
    /// Continue click cannot bypass the new continuation flow.
    /// <para/>
    /// <paramref name="restoredTruncated"/> is the inverse — set only by the restore path
    /// when the last assistant chain in a saved conversation was truncated. It does NOT
    /// draw a button; it merely prefixes the timestamp line with a discreet "끊긴 응답"
    /// hint so the user can choose to send "계속" themselves.
    /// </summary>
    public Task FinalizeMessageAsync(string id, string fullMarkdown, bool truncated = false, int tokenCount = 0,
        string? timestamp = null, double? elapsedSeconds = null, int coveredTokenCount = 0,
        bool restoredTruncated = false)
    {
        var tsArg = timestamp is not null ? Js(timestamp) : "null";
        var elapsedArg = elapsedSeconds.HasValue ? elapsedSeconds.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "null";
        var script = $"window.assistChat.finalizeMessage({Js(id)}, {Js(fullMarkdown)}, {(truncated ? "true" : "false")}, {tokenCount}, {tsArg}, {elapsedArg}, {coveredTokenCount}, {(restoredTruncated ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Applies CSS zoom and compensates container max-width to keep visual width at 800px.
    /// </summary>
    public Task ApplyZoomAsync(double zoomFactor)
    {
        var maxWidth = (int)Math.Round(800.0 / zoomFactor);
        var script = $"document.body.style.zoom='{zoomFactor}';"
            + $"document.getElementById('chat-container').style.maxWidth='{maxWidth}px';";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Scrolls the chat view to the bottom.
    /// </summary>
    public Task ScrollToBottomAsync()
    {
        return _webView.ExecuteScriptAsync("window.assistChat.scrollToBottom()").AsTask();
    }

    /// <summary>
    /// Removes all message elements that appear after the specified message ID in the chat UI.
    /// </summary>
    public Task RemoveMessagesAfterAsync(string messageId)
    {
        var script = $"window.assistChat.removeMessagesAfter({Js(messageId)})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Removes all messages from the chat display (for full re-render on branch switch).
    /// </summary>
    public Task ClearMessagesAsync()
    {
        return _webView.ExecuteScriptAsync(
            "document.getElementById('chat-container').innerHTML = ''").AsTask();
    }

    /// <summary>
    /// Updates the branch navigator on an existing message.
    /// </summary>
    public Task UpdateBranchNavAsync(string id, int siblingIndex, int siblingCount)
    {
        var script = $"window.assistChat.updateBranchNav({Js(id)}, {siblingIndex}, {siblingCount})";
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
        var script = $"window.assistChat.setTheme({(isDark ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Enables or disables debug mode, which shows "Copy Request" / "Copy Response" buttons.
    /// </summary>
    public Task SetDebugModeAsync(bool enabled)
    {
        var script = $"window.assistChat.setDebugMode({(enabled ? "true" : "false")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Stores debug data (raw request body and response) for a user/assistant message pair.
    /// </summary>
    public Task SetDebugDataAsync(string userMsgId, string? requestBody, string assistantMsgId, string? rawResponse)
    {
        var script = $"window.assistChat.setDebugData({Js(userMsgId)}, {Js(requestBody ?? "")}, {Js(assistantMsgId)}, {Js(rawResponse ?? "")})";
        return _webView.ExecuteScriptAsync(script).AsTask();
    }

    /// <summary>
    /// Sets the font family for all chat message rendering.
    /// </summary>
    public Task SetFontFamilyAsync(string fontFamily)
    {
        var escaped = fontFamily.Replace("'", "\\'");
        return _webView.ExecuteScriptAsync($"document.body.style.fontFamily = '{escaped}';").AsTask();
    }

    /// <summary>
    /// Sets the base font size (in pixels) for chat message rendering.
    /// </summary>
    public Task SetFontSizeAsync(double fontSize)
    {
        return _webView.ExecuteScriptAsync(
            $"document.documentElement.style.setProperty('--font-size-base', '{fontSize}px');").AsTask();
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
    /// Intercepts navigation attempts and opens external URLs in the default browser.
    /// Allows only the initial about:blank and NavigateToString loads.
    /// File URIs are opened in the default app if they are within allowed directories.
    /// </summary>
    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var uri = args.Uri;

        // Allow the initial blank page and data: URIs used during NavigateToString initialization
        if (string.IsNullOrEmpty(uri) ||
            uri.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Block all other navigation (prevents blank screen when clicking relative-path links
        // such as [Source](filename.md) rendered by marked.js)
        args.Cancel = true;

        if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            OpenInDefaultBrowser(uri);
        }
        else if (uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var localPath = new Uri(uri).LocalPath;
                if (IsAllowedFilePath(localPath))
                    OpenInDefaultApp(localPath);
            }
            catch (UriFormatException)
            {
                // Malformed file URI — silently ignore
            }
        }
    }

    /// <summary>
    /// Enforces the CDN allow-list on every outbound WebView2 request (including iframes).
    /// The order below is the request-time decision tree:
    /// <list type="number">
    ///   <item><description><c>data:</c>, <c>blob:</c>, <c>about:</c> — internal schemes,
    ///   never leave the process; pass through.</description></item>
    ///   <item><description><c>assiststudio.temp</c> virtual host — local media folder
    ///   mapped via <see cref="CoreWebView2.SetVirtualHostNameToFolderMapping"/>;
    ///   pass through.</description></item>
    ///   <item><description><c>assiststudio.vendor</c> virtual host — JSX runtime bundle
    ///   served from embedded assembly resources by <see cref="TryServeVendorResource"/>;
    ///   responds with the matching script (200) or 404. The artifact-preview iframe
    ///   loads React, Babel, Tailwind, recharts, etc. through here so the feature works
    ///   air-gapped.</description></item>
    ///   <item><description>Image / Media / Font over https — any origin. Passive resources
    ///   that cannot execute attacker-controlled code; allowed for placeholder avatars,
    ///   embedded videos, and webfonts.</description></item>
    ///   <item><description>Everything else over https — must match
    ///   <see cref="AllowedCdnRoutes"/> (cdnjs.cloudflare.com fallback for stray model
    ///   output that hard-codes Anthropic's own CDN).</description></item>
    /// </list>
    /// Anything else is rejected with a synthesized 403 response.
    /// </summary>
    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var uri = args.Request.Uri;
        if (string.IsNullOrEmpty(uri))
            return;

        // Allow internal scheme requests (data:, blob:, about:) — these never leave the process.
        if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Allow the temp-media virtual host (mapped to the local TempRoot folder).
        if (uri.StartsWith("http://assiststudio.temp/", StringComparison.OrdinalIgnoreCase) ||
            uri.StartsWith("https://assiststudio.temp/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri) ||
            parsedUri.Scheme != Uri.UriSchemeHttps)
        {
            // Not https — block. (file:/ftp:/raw http: get rejected; chat.html itself
            // is loaded via NavigateToString which is data:-equivalent and exempt above.)
            args.Response = sender.Environment.CreateWebResourceResponse(
                Content: null, StatusCode: 403, ReasonPhrase: "Forbidden",
                Headers: "Content-Type: text/plain");
            return;
        }

        // JSX vendor bundle (React, Babel, Tailwind, recharts, …) served from
        // embedded assembly resources via the assiststudio.vendor virtual host.
        // Never touches the network — works air-gapped and pins exact versions.
        if (TryServeVendorResource(parsedUri, args, sender.Environment))
            return;

        // Passive resources (image/media/font) — allow from any https origin. They cannot
        // execute attacker-controlled code; the URL is in the visible artifact source so
        // there's no covert exfiltration channel beyond a tracking-pixel ping.
        switch (args.ResourceContext)
        {
            case CoreWebView2WebResourceContext.Image:
            case CoreWebView2WebResourceContext.Media:
            case CoreWebView2WebResourceContext.Font:
                return;
        }

        foreach (var (host, pathPrefix) in AllowedCdnRoutes)
        {
            if (string.Equals(parsedUri.Host, host, StringComparison.OrdinalIgnoreCase) &&
                (pathPrefix.Length == 0 ||
                 parsedUri.AbsolutePath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
        }

        DiagnosticLogger.LogWarning(
            $"[ChatPanel] Blocked {args.ResourceContext} request to non-whitelisted URL: {uri}");
        args.Response = sender.Environment.CreateWebResourceResponse(
            Content: null, StatusCode: 403, ReasonPhrase: "Forbidden",
            Headers: "Content-Type: text/plain");
    }

    /// <summary>
    /// Intercepts download requests from native audio/video controls (3-dot menu → Download)
    /// and routes them through the ImageSaveRequested event for FileSavePicker handling.
    /// </summary>
    private void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        args.Cancel = true;
        var uri = args.DownloadOperation.Uri;
        if (!string.IsNullOrEmpty(uri))
            ImageSaveRequested?.Invoke(this, uri);
    }

    /// <summary>
    /// Logs WebView2 process failures so iframe / browser-process crashes
    /// don't bubble up as opaque <c>SEHException</c> in the host main loop.
    /// Most artifact-iframe crashes (file picker, GPU driver, OOM under
    /// heavy artifacts) land here. Logging the kind + reason + exit code
    /// lets us tell a sandbox-permission issue ("file picker crashed render
    /// process") apart from a real bug.
    /// </summary>
    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        var frameCount = 0;
        try { frameCount = args.FrameInfosForFailedProcess?.Count ?? 0; }
        catch { /* property can throw on certain failure kinds — ignore */ }

        DiagnosticLogger.LogWarning(
            $"[ChatPanel] WebView2 process failed: kind={args.ProcessFailedKind}, " +
            $"reason={args.Reason}, exitCode={args.ExitCode}, " +
            $"desc='{args.ProcessDescription}', module='{args.FailureSourceModulePath}', " +
            $"affectedFrames={frameCount}");
    }

    /// <summary>
    /// Intercepts target="_blank" links and opens them in the default browser.
    /// </summary>
    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        OpenInDefaultBrowser(args.Uri);
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
            else if (message?.StartsWith("edit-request:") == true)
            {
                var messageId = message["edit-request:".Length..];
                EditRequested?.Invoke(this, messageId);
            }
            else if (message?.StartsWith("branch:") == true)
            {
                var payload = message["branch:".Length..];
                var colonIdx = payload.IndexOf(':');
                if (colonIdx > 0)
                {
                    var messageId = payload[..colonIdx];
                    if (int.TryParse(payload[(colonIdx + 1)..], out var direction))
                        BranchSwitchRequested?.Invoke(this, (messageId, direction));
                }
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
            else if (message?.StartsWith("saveImageUrl:") == true)
                ImageSaveRequested?.Invoke(this, message["saveImageUrl:".Length..]);
            else if (message?.StartsWith("saveImage:") == true)
                ImageSaveRequested?.Invoke(this, message["saveImage:".Length..]);
            else if (message?.StartsWith("copyImageUrl:") == true)
                ImageCopyRequested?.Invoke(this, message["copyImageUrl:".Length..]);
            else if (message?.StartsWith("copyImage:") == true)
                ImageCopyRequested?.Invoke(this, message["copyImage:".Length..]);
            else if (message?.StartsWith("saveMedia:") == true)
                ImageSaveRequested?.Invoke(this, message["saveMedia:".Length..]);
            else if (message?.StartsWith("save-svg:") == true)
                DiagramSvgSaveRequested?.Invoke(this, message["save-svg:".Length..]);
            else if (message?.StartsWith("copy-svg:") == true)
                DiagramSvgCopyRequested?.Invoke(this, message["copy-svg:".Length..]);
            else if (message?.StartsWith("copy-text:") == true)
                CopyToClipboard(message["copy-text:".Length..]);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Opens the specified URL in the user's default web browser.
    /// </summary>
    private static void OpenInDefaultBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
        }
    }

    /// <summary>
    /// Opens a local file in the OS default application via shell execution.
    /// </summary>
    private static void OpenInDefaultApp(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
        }
    }

    /// <summary>
    /// Checks whether the given local file path is within allowed directories
    /// (temp root or workspace folders). Normalizes the path to prevent traversal attacks.
    /// </summary>
    private bool IsAllowedFilePath(string localPath)
    {
        try
        {
            var normalized = Path.GetFullPath(localPath);

            if (normalized.StartsWith(TempRoot, StringComparison.OrdinalIgnoreCase))
                return true;

            if (WorkspaceFolders is { } folders)
            {
                foreach (var root in folders)
                {
                    var normalizedRoot = Path.GetFullPath(root);
                    if (normalized.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Link] Failed to validate file path: {ex.Message}");
        }

        return false;
    }

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

        // Inject Mermaid + initialize with current theme
        var mermaidJs = LoadEmbeddedResource("mermaid.min.js");
        await _webView.ExecuteScriptAsync(mermaidJs).AsTask();
        const string mermaidInit = """
            (function() {
                if (typeof mermaid === 'undefined') return;
                var isDark = document.documentElement.getAttribute('data-theme') === 'dark';
                mermaid.initialize({
                    startOnLoad: false,
                    theme: isDark ? 'dark' : 'default',
                    securityLevel: 'loose',
                    fontFamily: 'inherit'
                });
            })();
            """;
        await _webView.ExecuteScriptAsync(mermaidInit).AsTask();

        // Configure marked with highlight.js + KaTeX math extensions.
        // Uses $$"""...""" so {{Name}} interpolates the artifact-preview JS
        // blocks that live in WebViewChatRenderer.ArtifactPreview.cs while
        // single { / } in the JS body stay literal.
        var configScript = $$"""
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

                // Artifact-preview helpers (JSX import map, lib registry,
                // shadcn shim, transformJsxArtifact, utf8ToBase64) live in
                // WebViewChatRenderer.ArtifactPreview.cs.
                {{ArtifactPreviewHelpersJs}}

                // Custom renderer: syntax-highlight code blocks via hljs
                var renderer = new marked.Renderer();
                renderer.code = function(token) {
                    var code = token.text || '';
                    var lang = (token.lang || '').trim();

                    // Header used by HTML/JSX preview blocks: a Preview/Code
                    // segmented toggle plus a Copy button. The block-level
                    // attribute data-view drives which child (.html-frame or
                    // .code-view) is visible — see chat.html CSS.
                    // themeForToggle is 'light'|'dark' for JSX (renders a
                    // sun/moon button that flips the iframe theme via
                    // postMessage), or empty/null for HTML (raw user HTML
                    // controls its own theme — no override possible).
                    // externalNote (optional, JSX only) is a pre-built
                    // human-readable string like "Loaded externally: plotly
                    // (esm.sh)" rendered muted between the language label
                    // and the action group. Empty string = no indicator.
                    function previewHeader(label, copyTooltip, themeForToggle, externalNote) {
                        var L = window._L || {};
                        var themeBtn = '';
                        if (themeForToggle) {
                            // ☀ U+2600 / ☾ U+263E — BMP text glyphs (avoid emoji
                            // rendering inconsistencies of 🌙 U+1F319). Shown icon
                            // reflects the CURRENT theme; clicking flips it.
                            var icon = (themeForToggle === 'dark') ? '☾' : '☀';
                            themeBtn = '<button class="diagram-btn theme-toggle-btn" data-act="theme" title="' +
                                        (L.previewThemeToggleTooltip || 'Toggle theme') + '">' +
                                        icon + '</button>';
                        }
                        var extInfo = '';
                        if (externalNote) {
                            // Inline style instead of a stylesheet rule so the marker is
                            // self-contained — no chat.html CSS dependency to track.
                            var escaped = String(externalNote)
                                .replace(/&/g,'&amp;').replace(/</g,'&lt;')
                                .replace(/>/g,'&gt;').replace(/"/g,'&quot;');
                            extInfo = '<span class="ext-libs-note" title="' + escaped + '" ' +
                                      'style="font-style:italic;opacity:0.65;font-size:0.85em;' +
                                      'margin-left:10px;overflow:hidden;text-overflow:ellipsis;' +
                                      'white-space:nowrap;max-width:40%;">' + escaped + '</span>';
                        }
                        return '<div class="code-header">' +
                                '<span class="code-lang">' + label + '</span>' +
                                extInfo +
                                '<span class="diagram-actions">' +
                                    '<span class="view-toggle-group">' +
                                        '<button class="diagram-btn view-toggle-btn active" data-view="preview">' + (L.previewToggleLabel || 'Preview') + '</button>' +
                                        '<button class="diagram-btn view-toggle-btn" data-view="code">' + (L.codeToggleLabel || 'Code') + '</button>' +
                                    '</span>' +
                                    themeBtn +
                                    '<button class="diagram-btn" data-act="copy" title="' + copyTooltip + '">' + (L.diagramCopyLabel || 'Copy') + '</button>' +
                                '</span>' +
                               '</div>';
                    }

                    // Diagram blocks (Mermaid / SVG): wrap with the same .code-header
                    // structure as code blocks so the SVG/PNG/Copy actions feel native.
                    function diagramHeader(label) {
                        var L = window._L || {};
                        return '<div class="code-header">' +
                                '<span class="code-lang">' + label + '</span>' +
                                '<span class="diagram-actions">' +
                                    '<button class="diagram-btn" data-act="svg" title="' + (L.diagramSaveSvg || 'Save as SVG') + '">SVG</button>' +
                                    '<button class="diagram-btn" data-act="png" title="' + (L.diagramSavePng || 'Save as PNG') + '">PNG</button>' +
                                    '<button class="diagram-btn" data-act="copy" title="' + (L.diagramCopyTooltip || 'Copy SVG source') + '">' + (L.diagramCopyLabel || 'Copy') + '</button>' +
                                '</span>' +
                               '</div>';
                    }

                    if (lang === 'mermaid') {
                        var entityEscaped = code
                            .replace(/&/g, '&amp;')
                            .replace(/</g, '&lt;')
                            .replace(/>/g, '&gt;');
                        return '<div class="diagram-block" data-kind="mermaid">' +
                                diagramHeader('mermaid') +
                                '<pre class="mermaid">' + entityEscaped + '</pre>' +
                               '</div>';
                    }

                    // SVG: render inline. Strip <script> tags to prevent XSS from
                    // model- or user-authored markup. Other vectors (event handler
                    // attributes, javascript: URLs) are not currently sanitized;
                    // accept a small risk in exchange for inline diagram rendering.
                    if (lang === 'svg') {
                        var sanitized = code.replace(/<script[\s\S]*?<\/script>/gi, '');
                        return '<div class="diagram-block" data-kind="svg">' +
                                diagramHeader('svg') +
                                '<div class="svg-block">' + sanitized + '</div>' +
                               '</div>';
                    }

                    // HTML: render inside a sandboxed iframe (allow-scripts only —
                    // no same-origin, so the artifact cannot reach parent DOM,
                    // cookies, or localStorage). Scripts and styles inside the
                    // snippet still execute against the iframe's own document.
                    // Branch body lives in WebViewChatRenderer.ArtifactPreview.cs.
                    {{HtmlPreviewBranchJs}}

                    // JSX / TSX: wrap user code in a React + Babel + Tailwind host
                    // shell, then render through the same sandboxed iframe pipeline
                    // as HTML. Imports are remapped to UMD globals; `export default`
                    // is hoisted to window.__default_export for the auto-mount.
                    // All scripts load from the assiststudio.vendor virtual host
                    // (embedded assembly resources — no network access required).
                    //
                    // Babel runs programmatically (not auto via type="text/babel")
                    // so we can catch parse/transform errors and surface them in
                    // the on-iframe error banner instead of silently failing.
                    {{JsxPreviewBranchJs}}

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
        css = KatexWoff2Regex().Replace(css, match =>
        {
            var fileName = match.Groups[1].Value;
            var fontBytes = LoadEmbeddedBinaryResource(fileName);
            var base64 = Convert.ToBase64String(fontBytes);
            return $"url(data:font/woff2;base64,{base64})";
        });

        // Strip .woff and .ttf src alternatives (woff2 is sufficient for WebView2)
        css = KatexWoffRegex().Replace(css, "");
        css = KatexTtfRegex().Replace(css, "");

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
            var type = att.Type == AttachmentType.Image ? "image"
                : att.Type == AttachmentType.TextFile ? "text"
                : att.Type == AttachmentType.Audio ? "audio"
                : att.Type == AttachmentType.Document ? "document"
                : "file";

            var obj = new JsonObject
            {
                ["fileName"] = att.FileName,
                ["type"] = type,
                ["mimeType"] = att.MimeType ?? "application/octet-stream"
            };

            if (att.Type == AttachmentType.Image)
            {
                obj["base64"] = Convert.ToBase64String(att.Data);
            }
            else if (att.Type == AttachmentType.Audio)
            {
                obj["sizeBytes"] = att.Data.LongLength;
                if (att.Duration is { } d && d > TimeSpan.Zero)
                {
                    obj["durationSeconds"] = (long)d.TotalSeconds;
                }
            }
            else if (att.Type == AttachmentType.Document)
            {
                obj["sizeBytes"] = att.Data.LongLength;
            }
            else if (att.Type == AttachmentType.TextFile)
            {
                obj["source"] = att.Source == AttachmentSource.Pasted ? "pasted" : "file";

                var fullText = Encoding.UTF8.GetString(att.Data);
                obj["charCount"] = att.CharCount > 0 ? att.CharCount : fullText.Length;
                obj["lineCount"] = att.LineCount > 0 ? att.LineCount : fullText.AsSpan().Count('\n') + 1;

                // Full content with 5MB clamp (UTF-8 safe slice at boundary)
                const int MaxFullBytes = 5 * 1024 * 1024;
                const int ClampedBytes = 100 * 1024;
                if (att.Data.Length <= MaxFullBytes)
                {
                    obj["fullContent"] = fullText;
                    obj["isTruncated"] = false;
                }
                else
                {
                    var clampedSlice = TextAttachmentHelper.SafeUtf8Slice(att.Data, ClampedBytes);
                    obj["fullContent"] = Encoding.UTF8.GetString(clampedSlice);
                    obj["isTruncated"] = true;
                }
            }

            array.Add(obj);
        }

        return array.ToJsonString(WebViewJsonOptions);
    }

    /// <summary>
    /// JSON options for serializing values injected into WebView2 script.
    /// Uses relaxed escaping so non-ASCII characters (Korean, emoji, etc.)
    /// are emitted as-is instead of \uXXXX sequences.
    /// </summary>
    private static readonly JsonSerializerOptions WebViewJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// JSON-escapes a string value for safe interpolation into JavaScript code.
    /// Uses relaxed encoding to preserve non-ASCII characters.
    /// </summary>
    private static string Js(string value) => JsonSerializer.Serialize(value, WebViewJsonOptions);

    #endregion

    #region Generated Regex

    /// <summary>Matches KaTeX woff2 font URLs for base64 embedding.</summary>
    [GeneratedRegex(@"url\(fonts/(KaTeX_[^)]+\.woff2)\)")]
    private static partial Regex KatexWoff2Regex();

    /// <summary>Matches KaTeX woff font src alternatives to strip.</summary>
    [GeneratedRegex(@",url\(fonts/[^)]+\.woff\)\s*format\(""woff""\)")]
    private static partial Regex KatexWoffRegex();

    /// <summary>Matches KaTeX ttf font src alternatives to strip.</summary>
    [GeneratedRegex(@",url\(fonts/[^)]+\.ttf\)\s*format\(""truetype""\)")]
    private static partial Regex KatexTtfRegex();

    #endregion
}
