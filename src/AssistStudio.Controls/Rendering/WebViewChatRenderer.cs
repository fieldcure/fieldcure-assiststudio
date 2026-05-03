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
    /// </summary>
    public Task BeginAssistantMessageAsync(string id, string? providerName = null, string? modelId = null,
        bool isSummary = false)
    {
        var script = $"window.assistChat.beginAssistantMessage({Js(id)}, {Js(providerName ?? "")}, {Js(modelId ?? "")}, {(isSummary ? "true" : "false")})";
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
    /// </summary>
    public Task FinalizeMessageAsync(string id, string fullMarkdown, bool truncated = false, int tokenCount = 0,
        string? timestamp = null, double? elapsedSeconds = null, int coveredTokenCount = 0)
    {
        var tsArg = timestamp is not null ? Js(timestamp) : "null";
        var elapsedArg = elapsedSeconds.HasValue ? elapsedSeconds.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "null";
        var script = $"window.assistChat.finalizeMessage({Js(id)}, {Js(fullMarkdown)}, {(truncated ? "true" : "false")}, {tokenCount}, {tsArg}, {elapsedArg}, {coveredTokenCount})";
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

                // ---- JSX/TSX preview helpers ----
                // Maps ES module imports to their UMD globals exposed by the host shell.
                var JSX_IMPORT_MAP = {
                    'react': 'React',
                    'react-dom': 'ReactDOM',
                    'react-dom/client': 'ReactDOM',
                    'recharts': 'Recharts',
                    'lucide-react': 'lucideReact',
                    'd3': 'd3',
                    'three': 'THREE',
                    'lodash': '_',
                    'mathjs': 'math',
                    'papaparse': 'Papa',
                    'chart.js': 'Chart',
                    'tone': 'Tone'
                };

                // CDN script URLs for libraries beyond React/ReactDOM. React itself,
                // Babel, and Tailwind are always loaded; everything here is opt-in
                // based on what the user code actually imports.
                var JSX_LIB_CDN = {
                    'lucide-react': 'https://unpkg.com/lucide-react@0.383.0/dist/umd/lucide-react.min.js',
                    'recharts':     'https://unpkg.com/recharts@2.12.7/umd/Recharts.js',
                    'd3':           'https://unpkg.com/d3@7.9.0/dist/d3.min.js',
                    'three':        'https://unpkg.com/three@0.128.0/build/three.min.js',
                    'lodash':       'https://unpkg.com/lodash@4.17.21/lodash.min.js',
                    'mathjs':       'https://unpkg.com/mathjs@13.2.0/lib/browser/math.js',
                    'papaparse':    'https://unpkg.com/papaparse@5.4.1/papaparse.min.js',
                    'chart.js':     'https://unpkg.com/chart.js@4.4.4/dist/chart.umd.js',
                    'tone':         'https://unpkg.com/tone@15.0.4/build/Tone.js'
                };

                // Implicit dependencies — UMD bundles that require other globals
                // beyond React/ReactDOM. Loaded as a side-effect of pulling the
                // primary lib (e.g. recharts inspects window.PropTypes for its
                // internal validators).
                var JSX_LIB_DEPS = {
                    'recharts': ['prop-types']
                };
                var JSX_DEP_CDN = {
                    'prop-types': 'https://unpkg.com/prop-types@15.8.1/prop-types.min.js'
                };

                // Some UMD bundles expect React on a differently-cased global
                // (lucide-react@0.383 looks up `window.react` rather than the
                // canonical `window.React`). Run before any lib script.
                var JSX_PRE_LIB_SHIM_JS =
                    'window.react = window.React;' +
                    'window.reactDom = window.ReactDOM;';

                // After UMD libs initialize, normalize their exported globals to
                // the names our import map expects. lucide-react@0.383 exposes
                // `LucideReact`; older builds expose `lucide`.
                var JSX_POST_LIB_SHIM_JS =
                    'window.lucideReact = window.lucideReact || window.LucideReact || window.lucide;' +
                    'window.Recharts = window.Recharts || window.recharts;' +
                    'window.THREE = window.THREE || window.three;';

                // shadcn/ui CSS variables (HSL triplets) — lets `bg-primary`,
                // `text-muted-foreground`, etc. resolve to actual colors. Light
                // theme on :root, dark via [data-theme="dark"] (matches our
                // tailwind config below).
                var SHADCN_CSS_VARS_CSS =
                    ':root{' +
                        '--background:0 0% 100%;--foreground:222.2 84% 4.9%;' +
                        '--card:0 0% 100%;--card-foreground:222.2 84% 4.9%;' +
                        '--popover:0 0% 100%;--popover-foreground:222.2 84% 4.9%;' +
                        '--primary:222.2 47.4% 11.2%;--primary-foreground:210 40% 98%;' +
                        '--secondary:210 40% 96.1%;--secondary-foreground:222.2 47.4% 11.2%;' +
                        '--muted:210 40% 96.1%;--muted-foreground:215.4 16.3% 46.9%;' +
                        '--accent:210 40% 96.1%;--accent-foreground:222.2 47.4% 11.2%;' +
                        '--destructive:0 84.2% 60.2%;--destructive-foreground:210 40% 98%;' +
                        '--border:214.3 31.8% 91.4%;--input:214.3 31.8% 91.4%;--ring:222.2 84% 4.9%;' +
                        '--radius:0.5rem;' +
                    '}' +
                    '[data-theme="dark"]{' +
                        '--background:222.2 84% 4.9%;--foreground:210 40% 98%;' +
                        '--card:222.2 84% 4.9%;--card-foreground:210 40% 98%;' +
                        '--popover:222.2 84% 4.9%;--popover-foreground:210 40% 98%;' +
                        '--primary:210 40% 98%;--primary-foreground:222.2 47.4% 11.2%;' +
                        '--secondary:217.2 32.6% 17.5%;--secondary-foreground:210 40% 98%;' +
                        '--muted:217.2 32.6% 17.5%;--muted-foreground:215 20.2% 65.1%;' +
                        '--accent:217.2 32.6% 17.5%;--accent-foreground:210 40% 98%;' +
                        '--destructive:0 62.8% 30.6%;--destructive-foreground:210 40% 98%;' +
                        '--border:217.2 32.6% 17.5%;--input:217.2 32.6% 17.5%;--ring:212.7 26.8% 83.9%;' +
                    '}';

                // Tailwind config that wires the shadcn HSL vars into utility
                // classes (`bg-primary`, `text-muted-foreground`, …). Set via
                // the global `tailwind.config` AFTER the CDN tailwind script
                // loads and BEFORE the user code paints anything.
                var SHADCN_TAILWIND_CONFIG_JS =
                    'if(window.tailwind){tailwind.config={' +
                        'darkMode:["class","[data-theme=\\"dark\\"]"],' +
                        'theme:{extend:{' +
                            'colors:{' +
                                'border:"hsl(var(--border))",input:"hsl(var(--input))",ring:"hsl(var(--ring))",' +
                                'background:"hsl(var(--background))",foreground:"hsl(var(--foreground))",' +
                                'primary:{DEFAULT:"hsl(var(--primary))",foreground:"hsl(var(--primary-foreground))"},' +
                                'secondary:{DEFAULT:"hsl(var(--secondary))",foreground:"hsl(var(--secondary-foreground))"},' +
                                'destructive:{DEFAULT:"hsl(var(--destructive))",foreground:"hsl(var(--destructive-foreground))"},' +
                                'muted:{DEFAULT:"hsl(var(--muted))",foreground:"hsl(var(--muted-foreground))"},' +
                                'accent:{DEFAULT:"hsl(var(--accent))",foreground:"hsl(var(--accent-foreground))"},' +
                                'popover:{DEFAULT:"hsl(var(--popover))",foreground:"hsl(var(--popover-foreground))"},' +
                                'card:{DEFAULT:"hsl(var(--card))",foreground:"hsl(var(--card-foreground))"}' +
                            '},' +
                            'borderRadius:{lg:"var(--radius)",md:"calc(var(--radius) - 2px)",sm:"calc(var(--radius) - 4px)"}' +
                        '}}' +
                    '};}';

                // shadcn/ui stub: exposes the ~25 most common components on
                // `window.__shadcn`. Visual fidelity over behavioral fidelity —
                // composed primitives render as plain Tailwind-styled divs;
                // controlled inputs (Switch/Checkbox/Tabs/Accordion) carry real
                // useState. Stubs for Dialog/Popover/Tooltip/Select fall back
                // to inline rendering of their children so layout is preserved
                // even though the modal/floating behavior is missing.
                var SHADCN_SHIM_JS =
                    "(function(R){" +
                    "if(!R){return;}" +
                    "var h=R.createElement,uS=R.useState,uId=R.useId||function(){return 'id-'+Math.random().toString(36).slice(2,9);};" +
                    "function cn(){var a=arguments,p=[],i;for(i=0;i<a.length;i++){if(a[i]&&typeof a[i]==='string')p.push(a[i]);}return p.join(' ');}" +
                    "function pass(props){var o={},k;for(k in props){if(k!=='children'&&k!=='className'&&k!=='variant'&&k!=='size'&&k!=='asChild')o[k]=props[k];}return o;}" +
                    // ---- primitives ----
                    "var Button=function(p){p=p||{};var v=p.variant||'default',s=p.size||'default';" +
                        "var vc={'default':'bg-primary text-primary-foreground hover:opacity-90','destructive':'bg-destructive text-destructive-foreground hover:opacity-90','outline':'border border-input bg-background hover:bg-accent hover:text-accent-foreground','secondary':'bg-secondary text-secondary-foreground hover:opacity-80','ghost':'hover:bg-accent hover:text-accent-foreground','link':'text-primary underline-offset-4 hover:underline'};" +
                        "var sc={'default':'h-10 px-4 py-2','sm':'h-9 px-3','lg':'h-11 px-8','icon':'h-10 w-10'};" +
                        "return h('button',Object.assign({className:cn('inline-flex items-center justify-center gap-2 rounded-md text-sm font-medium transition-colors disabled:pointer-events-none disabled:opacity-50',vc[v]||vc['default'],sc[s]||sc['default'],p.className)},pass(p)),p.children);};" +
                    "var Card=function(p){p=p||{};return h('div',Object.assign({className:cn('rounded-lg border bg-card text-card-foreground shadow-sm',p.className)},pass(p)),p.children);};" +
                    "var CardHeader=function(p){p=p||{};return h('div',Object.assign({className:cn('flex flex-col space-y-1.5 p-6',p.className)},pass(p)),p.children);};" +
                    "var CardTitle=function(p){p=p||{};return h('h3',Object.assign({className:cn('text-2xl font-semibold leading-none tracking-tight',p.className)},pass(p)),p.children);};" +
                    "var CardDescription=function(p){p=p||{};return h('p',Object.assign({className:cn('text-sm text-muted-foreground',p.className)},pass(p)),p.children);};" +
                    "var CardContent=function(p){p=p||{};return h('div',Object.assign({className:cn('p-6 pt-0',p.className)},pass(p)),p.children);};" +
                    "var CardFooter=function(p){p=p||{};return h('div',Object.assign({className:cn('flex items-center p-6 pt-0',p.className)},pass(p)),p.children);};" +
                    "var Input=function(p){p=p||{};return h('input',Object.assign({className:cn('flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring disabled:opacity-50',p.className)},pass(p)));};" +
                    "var Label=function(p){p=p||{};return h('label',Object.assign({className:cn('text-sm font-medium leading-none peer-disabled:cursor-not-allowed peer-disabled:opacity-70',p.className)},pass(p)),p.children);};" +
                    "var Textarea=function(p){p=p||{};return h('textarea',Object.assign({className:cn('flex min-h-[80px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-ring disabled:opacity-50',p.className)},pass(p)));};" +
                    "var Badge=function(p){p=p||{};var v=p.variant||'default',vc={'default':'bg-primary text-primary-foreground','secondary':'bg-secondary text-secondary-foreground','destructive':'bg-destructive text-destructive-foreground','outline':'text-foreground border'};return h('div',Object.assign({className:cn('inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold',vc[v]||vc['default'],p.className)},pass(p)),p.children);};" +
                    "var Alert=function(p){p=p||{};var v=p.variant||'default',vc={'default':'bg-background text-foreground','destructive':'border-destructive/50 text-destructive [&>svg]:text-destructive'};return h('div',Object.assign({role:'alert',className:cn('relative w-full rounded-lg border p-4 [&>svg]:absolute [&>svg]:left-4 [&>svg]:top-4 [&>svg+div]:translate-y-[-3px] [&:has(svg)]:pl-11',vc[v]||vc['default'],p.className)},pass(p)),p.children);};" +
                    "var AlertTitle=function(p){p=p||{};return h('h5',Object.assign({className:cn('mb-1 font-medium leading-none tracking-tight',p.className)},pass(p)),p.children);};" +
                    "var AlertDescription=function(p){p=p||{};return h('div',Object.assign({className:cn('text-sm [&_p]:leading-relaxed',p.className)},pass(p)),p.children);};" +
                    "var Avatar=function(p){p=p||{};return h('span',Object.assign({className:cn('relative flex h-10 w-10 shrink-0 overflow-hidden rounded-full bg-muted',p.className)},pass(p)),p.children);};" +
                    "var AvatarImage=function(p){p=p||{};return h('img',Object.assign({className:cn('aspect-square h-full w-full',p.className)},pass(p)));};" +
                    "var AvatarFallback=function(p){p=p||{};return h('span',Object.assign({className:cn('flex h-full w-full items-center justify-center bg-muted text-sm font-medium',p.className)},pass(p)),p.children);};" +
                    "var Separator=function(p){p=p||{};var o=p.orientation||'horizontal';return h('div',Object.assign({className:cn('shrink-0 bg-border',o==='horizontal'?'h-px w-full':'h-full w-px',p.className)},pass(p)));};" +
                    "var Progress=function(p){p=p||{};var v=Math.max(0,Math.min(100,p.value||0));return h('div',Object.assign({className:cn('relative h-4 w-full overflow-hidden rounded-full bg-secondary',p.className)},pass(p)),h('div',{className:'h-full bg-primary transition-all',style:{width:v+'%'}}));};" +
                    "var Skeleton=function(p){p=p||{};return h('div',Object.assign({className:cn('animate-pulse rounded-md bg-muted',p.className)},pass(p)));};" +
                    // ---- controlled primitives ----
                    "var Switch=function(p){p=p||{};var ctrl=p.checked!==undefined,sa=uS(!!p.defaultChecked),checked=ctrl?p.checked:sa[0];" +
                        "function toggle(){var nv=!checked;if(!ctrl)sa[1](nv);if(p.onCheckedChange)p.onCheckedChange(nv);}" +
                        "return h('button',{type:'button',role:'switch','aria-checked':checked,onClick:toggle,className:cn('relative inline-flex h-6 w-11 shrink-0 cursor-pointer items-center rounded-full transition-colors',checked?'bg-primary':'bg-input',p.className)}," +
                            "h('span',{className:cn('pointer-events-none block h-5 w-5 rounded-full bg-background shadow-lg ring-0 transition-transform',checked?'translate-x-5':'translate-x-0.5')}));};" +
                    "var Checkbox=function(p){p=p||{};var ctrl=p.checked!==undefined,sa=uS(!!p.defaultChecked),checked=ctrl?p.checked:sa[0];" +
                        "function toggle(){var nv=!checked;if(!ctrl)sa[1](nv);if(p.onCheckedChange)p.onCheckedChange(nv);}" +
                        "return h('button',{type:'button',role:'checkbox','aria-checked':checked,onClick:toggle,className:cn('h-4 w-4 shrink-0 rounded-sm border border-primary inline-flex items-center justify-center',checked?'bg-primary text-primary-foreground':'bg-background',p.className)}," +
                            "checked?h('span',{className:'text-xs leading-none'},'\\u2714'):null);};" +
                    // ---- Tabs (context via React.createContext) ----
                    "var TabsCtx=R.createContext({value:null,setValue:function(){}});" +
                    "var Tabs=function(p){p=p||{};var ctrl=p.value!==undefined,sa=uS(p.defaultValue),value=ctrl?p.value:sa[0];" +
                        "function setValue(nv){if(!ctrl)sa[1](nv);if(p.onValueChange)p.onValueChange(nv);}" +
                        "return h(TabsCtx.Provider,{value:{value:value,setValue:setValue}},h('div',{className:cn(p.className)},p.children));};" +
                    "var TabsList=function(p){p=p||{};return h('div',Object.assign({className:cn('inline-flex h-10 items-center justify-center rounded-md bg-muted p-1 text-muted-foreground',p.className)},pass(p)),p.children);};" +
                    "var TabsTrigger=function(p){p=p||{};var ctx=R.useContext(TabsCtx),active=ctx.value===p.value;" +
                        "return h('button',{type:'button',onClick:function(){ctx.setValue(p.value);},className:cn('inline-flex items-center justify-center whitespace-nowrap rounded-sm px-3 py-1.5 text-sm font-medium transition-all',active?'bg-background text-foreground shadow-sm':'',p.className)},p.children);};" +
                    "var TabsContent=function(p){p=p||{};var ctx=R.useContext(TabsCtx);if(ctx.value!==p.value)return null;return h('div',Object.assign({className:cn('mt-2',p.className)},pass(p)),p.children);};" +
                    // ---- minimal stubs (graceful degradation, no portal/positioning) ----
                    "var inlineDiv=function(cls){return function(p){p=p||{};return h('div',Object.assign({className:cn(cls||'',p.className)},pass(p)),p.children);};};" +
                    "var Dialog=inlineDiv(),DialogTrigger=inlineDiv(),DialogContent=inlineDiv('rounded-lg border bg-background p-6 shadow-lg'),DialogHeader=inlineDiv('flex flex-col space-y-1.5 text-center sm:text-left'),DialogFooter=inlineDiv('flex flex-col-reverse sm:flex-row sm:justify-end sm:space-x-2'),DialogTitle=function(p){p=p||{};return h('h2',Object.assign({className:cn('text-lg font-semibold leading-none tracking-tight',p.className)},pass(p)),p.children);},DialogDescription=function(p){p=p||{};return h('p',Object.assign({className:cn('text-sm text-muted-foreground',p.className)},pass(p)),p.children);};" +
                    "var Popover=inlineDiv(),PopoverTrigger=inlineDiv(),PopoverContent=inlineDiv('rounded-md border bg-popover p-4 text-popover-foreground shadow-md');" +
                    "var Tooltip=inlineDiv(),TooltipProvider=inlineDiv(),TooltipTrigger=inlineDiv(),TooltipContent=inlineDiv('rounded-md bg-primary px-3 py-1.5 text-xs text-primary-foreground');" +
                    "var Select=inlineDiv(),SelectTrigger=function(p){p=p||{};return h('button',Object.assign({type:'button',className:cn('flex h-10 w-full items-center justify-between rounded-md border border-input bg-background px-3 py-2 text-sm',p.className)},pass(p)),p.children);},SelectValue=function(p){p=p||{};return h('span',Object.assign({className:p.className},pass(p)),p.placeholder||p.children);},SelectContent=inlineDiv('rounded-md border bg-popover p-1'),SelectItem=function(p){p=p||{};return h('div',Object.assign({className:cn('relative flex cursor-default select-none items-center rounded-sm py-1.5 px-2 text-sm hover:bg-accent',p.className)},pass(p)),p.children);};" +
                    "var DropdownMenu=inlineDiv(),DropdownMenuTrigger=inlineDiv(),DropdownMenuContent=inlineDiv('rounded-md border bg-popover p-1 shadow-md'),DropdownMenuItem=SelectItem,DropdownMenuSeparator=function(p){p=p||{};return h('div',Object.assign({className:cn('-mx-1 my-1 h-px bg-muted',p.className)},pass(p)));},DropdownMenuLabel=function(p){p=p||{};return h('div',Object.assign({className:cn('px-2 py-1.5 text-sm font-semibold',p.className)},pass(p)),p.children);};" +
                    "var Sheet=inlineDiv(),SheetTrigger=inlineDiv(),SheetContent=inlineDiv('rounded-lg border bg-background p-6 shadow-lg'),SheetHeader=DialogHeader,SheetFooter=DialogFooter,SheetTitle=DialogTitle,SheetDescription=DialogDescription;" +
                    "var Accordion=inlineDiv(),AccordionItem=inlineDiv('border-b'),AccordionTrigger=function(p){p=p||{};return h('button',Object.assign({type:'button',className:cn('flex flex-1 items-center justify-between py-4 font-medium hover:underline',p.className)},pass(p)),p.children);},AccordionContent=inlineDiv('pb-4 pt-0 text-sm');" +
                    // ---- expose ----
                    "window.__shadcn=Object.assign(window.__shadcn||{},{" +
                        "cn:cn,Button:Button," +
                        "Card:Card,CardHeader:CardHeader,CardTitle:CardTitle,CardDescription:CardDescription,CardContent:CardContent,CardFooter:CardFooter," +
                        "Input:Input,Label:Label,Textarea:Textarea," +
                        "Badge:Badge,Alert:Alert,AlertTitle:AlertTitle,AlertDescription:AlertDescription," +
                        "Avatar:Avatar,AvatarImage:AvatarImage,AvatarFallback:AvatarFallback," +
                        "Separator:Separator,Progress:Progress,Skeleton:Skeleton," +
                        "Switch:Switch,Checkbox:Checkbox," +
                        "Tabs:Tabs,TabsList:TabsList,TabsTrigger:TabsTrigger,TabsContent:TabsContent," +
                        "Dialog:Dialog,DialogTrigger:DialogTrigger,DialogContent:DialogContent,DialogHeader:DialogHeader,DialogFooter:DialogFooter,DialogTitle:DialogTitle,DialogDescription:DialogDescription," +
                        "Popover:Popover,PopoverTrigger:PopoverTrigger,PopoverContent:PopoverContent," +
                        "Tooltip:Tooltip,TooltipProvider:TooltipProvider,TooltipTrigger:TooltipTrigger,TooltipContent:TooltipContent," +
                        "Select:Select,SelectTrigger:SelectTrigger,SelectValue:SelectValue,SelectContent:SelectContent,SelectItem:SelectItem," +
                        "DropdownMenu:DropdownMenu,DropdownMenuTrigger:DropdownMenuTrigger,DropdownMenuContent:DropdownMenuContent,DropdownMenuItem:DropdownMenuItem,DropdownMenuSeparator:DropdownMenuSeparator,DropdownMenuLabel:DropdownMenuLabel," +
                        "Sheet:Sheet,SheetTrigger:SheetTrigger,SheetContent:SheetContent,SheetHeader:SheetHeader,SheetFooter:SheetFooter,SheetTitle:SheetTitle,SheetDescription:SheetDescription," +
                        "Accordion:Accordion,AccordionItem:AccordionItem,AccordionTrigger:AccordionTrigger,AccordionContent:AccordionContent" +
                    "});" +
                    "})(window.React);";

                /// Rewrites `import` and `export default` so user code runs under
                /// Babel-standalone (no ES module support) against the UMD globals
                /// the host shell loads from CDN. Unknown imports are stripped with
                /// a comment so Babel does not throw. Returns { code, libs } where
                /// `libs` is the list of import sources we should load via CDN.
                function transformJsxArtifact(src) {
                    function mapMod(mod) {
                        // shadcn convention: any '@/components/ui/...' or
                        // '@/lib/utils' resolves to the inlined window.__shadcn.
                        if (mod.indexOf('@/') === 0) return 'window.__shadcn';
                        return JSX_IMPORT_MAP[mod] || null;
                    }
                    // For default/namespace imports we must read the global as a
                    // property (window.X), not a binding. Otherwise patterns
                    // like `import * as d3 from "d3"` become `const d3 = d3;`
                    // and TDZ-fail because the RHS resolves to the const being
                    // declared. Dotted paths (e.g. window.__shadcn) are
                    // already property access, so leave as-is.
                    function asProperty(g) {
                        return g.indexOf('.') >= 0 ? g : 'window.' + g;
                    }

                    // First pass: collect every imported module that has a CDN entry.
                    var libsSeen = {};
                    var importScanRe = /^[ \t]*import\s+(?:[^'"]+?\s+from\s+)?['"]([^'"]+)['"];?/gm;
                    var scan;
                    while ((scan = importScanRe.exec(src)) !== null) {
                        if (JSX_LIB_CDN[scan[1]]) libsSeen[scan[1]] = true;
                    }
                    var libs = Object.keys(libsSeen);

                    // import { a, b as c } from "mod";
                    src = src.replace(
                        /^[ \t]*import\s*\{\s*([^}]+?)\s*\}\s*from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                        function(_m, names, mod) {
                            var g = mapMod(mod);
                            if (!g) return '/* import { ' + names + " } from '" + mod + "' (unmapped, stripped) */";
                            var dest = names.split(',').map(function(p) {
                                p = p.trim();
                                var asMatch = p.match(/^(\w+)\s+as\s+(\w+)$/);
                                return asMatch ? asMatch[1] + ': ' + asMatch[2] : p;
                            }).join(', ');
                            return 'const { ' + dest + ' } = ' + g + ';';
                        });

                    // import * as X from "mod";
                    src = src.replace(
                        /^[ \t]*import\s*\*\s*as\s+(\w+)\s+from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                        function(_m, name, mod) {
                            var g = mapMod(mod);
                            if (!g) return "/* import * as " + name + " from '" + mod + "' (unmapped, stripped) */";
                            return 'const ' + name + ' = ' + asProperty(g) + ';';
                        });

                    // import X from "mod";
                    src = src.replace(
                        /^[ \t]*import\s+(\w+)\s+from\s*['"]([^'"]+)['"];?[ \t]*$/gm,
                        function(_m, name, mod) {
                            var g = mapMod(mod);
                            if (!g) return "/* import " + name + " from '" + mod + "' (unmapped, stripped) */";
                            var p = asProperty(g);
                            return 'const ' + name + ' = ' + p + '.default || ' + p + ';';
                        });

                    // import "mod";  (side-effect only — drop)
                    src = src.replace(/^[ \t]*import\s+['"][^'"]+['"];?[ \t]*$/gm, '');

                    // export default function NAMED  →  named function expression on window
                    src = src.replace(/^export\s+default\s+function(\s+\w+)?/gm,
                        'window.__default_export = function$1');
                    // export default class NAMED  →  named class expression on window
                    src = src.replace(/^export\s+default\s+class(\s+\w+)?/gm,
                        'window.__default_export = class$1');
                    // export default IDENTIFIER;
                    src = src.replace(/^export\s+default\s+(\w+)\s*;?[ \t]*$/gm,
                        'window.__default_export = $1;');
                    // export default <expr>  (arrow funcs, JSX literals, etc.)
                    src = src.replace(/^export\s+default\s+/gm, 'window.__default_export = ');
                    // export const / export function / etc.  →  drop the keyword
                    src = src.replace(/^export\s+(?!default)/gm, '');

                    return { code: src, libs: libs };
                }

                /// UTF-8 safe base64 encode for stashing original JSX source on a
                /// data attribute (so the Copy button returns the verbatim source,
                /// not the host-shell-wrapped iframe document).
                function utf8ToBase64(str) {
                    return btoa(unescape(encodeURIComponent(str)));
                }

                // Custom renderer: syntax-highlight code blocks via hljs
                var renderer = new marked.Renderer();
                renderer.code = function(token) {
                    var code = token.text || '';
                    var lang = (token.lang || '').trim();

                    // Header used by HTML/JSX preview blocks: a Preview/Code
                    // segmented toggle plus a Copy button. The block-level
                    // attribute data-view drives which child (.html-frame or
                    // .code-view) is visible — see chat.html CSS.
                    function previewHeader(label, copyTooltip) {
                        var L = window._L || {};
                        return '<div class="code-header">' +
                                '<span class="code-lang">' + label + '</span>' +
                                '<span class="diagram-actions">' +
                                    '<span class="view-toggle-group">' +
                                        '<button class="diagram-btn view-toggle-btn active" data-view="preview">' + (L.previewToggleLabel || 'Preview') + '</button>' +
                                        '<button class="diagram-btn view-toggle-btn" data-view="code">' + (L.codeToggleLabel || 'Code') + '</button>' +
                                    '</span>' +
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
                    if (lang === 'html') {
                        var L = window._L || {};
                        var attrEscaped = code
                            .replace(/&/g, '&amp;')
                            .replace(/"/g, '&quot;')
                            .replace(/</g, '&lt;')
                            .replace(/>/g, '&gt;');
                        var htmlHl;
                        try { htmlHl = hljs.highlight(code, { language: 'html' }).value; }
                        catch(e) { htmlHl = code.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
                        return '<div class="diagram-block" data-kind="html" data-view="preview">' +
                                previewHeader('html', L.htmlPreviewCopyTooltip || 'Copy HTML source') +
                                '<div class="html-frame">' +
                                    '<iframe sandbox="allow-scripts" srcdoc="' + attrEscaped + '"></iframe>' +
                                '</div>' +
                                '<pre class="code-view"><code class="hljs language-html">' + htmlHl + '</code></pre>' +
                               '</div>';
                    }

                    // JSX / TSX: wrap user code in a React + Babel + Tailwind host
                    // shell, then render through the same sandboxed iframe pipeline
                    // as HTML. Imports are remapped to UMD globals; `export default`
                    // is hoisted to window.__default_export for the auto-mount.
                    // Scripts come from public CDNs (unpkg, cdn.tailwindcss.com).
                    //
                    // Babel runs programmatically (not auto via type="text/babel")
                    // so we can catch parse/transform errors and surface them in
                    // the on-iframe error banner instead of silently failing.
                    if (lang === 'jsx' || lang === 'tsx') {
                        var L2 = window._L || {};
                        var jsxResult = transformJsxArtifact(code);
                        var transformedB64 = utf8ToBase64(jsxResult.code);
                        var presets = (lang === 'tsx') ? 'react,typescript' : 'react';
                        // Order: each lib's deps first, then the lib itself.
                        // De-dupe so two libs sharing a dep don't double-load.
                        var loadedScripts = {};
                        var libScriptList = [];
                        jsxResult.libs.forEach(function(lib) {
                            (JSX_LIB_DEPS[lib] || []).forEach(function(dep) {
                                var depUrl = JSX_DEP_CDN[dep];
                                if (depUrl && !loadedScripts[depUrl]) {
                                    loadedScripts[depUrl] = true;
                                    libScriptList.push('<script src="' + depUrl + '" crossorigin></script>');
                                }
                            });
                            var url = JSX_LIB_CDN[lib];
                            if (url && !loadedScripts[url]) {
                                loadedScripts[url] = true;
                                libScriptList.push('<script src="' + url + '" crossorigin></script>');
                            }
                        });
                        var libScripts = libScriptList.join('');

                        // Installed first so it can capture script load failures
                        // (script.onerror bubbles via capture phase) and any
                        // runtime/promise errors, including ones in CDN libs.
                        var errorInfraJs =
                            '(function(){' +
                                'var buf=[];' +
                                'window.__showPreviewError=function(m){' +
                                    'if(!document.body){buf.push(m);return;}' +
                                    'var d=document.getElementById("__preview_error");' +
                                    'if(!d){d=document.createElement("div");d.id="__preview_error";' +
                                        'var r=document.getElementById("root");' +
                                        'document.body.insertBefore(d,r||document.body.firstChild);}' +
                                    'd.appendChild(document.createTextNode(m+"\\n"));' +
                                '};' +
                                'window.addEventListener("error",function(e){' +
                                    'if(e.target&&(e.target.tagName==="SCRIPT"||e.target.tagName==="LINK"))' +
                                        'window.__showPreviewError("Failed to load: "+(e.target.src||e.target.href));' +
                                    'else ' +
                                        'window.__showPreviewError((e.message||"Error")+(e.filename?" @ "+e.filename+":"+e.lineno:""));' +
                                '},true);' +
                                'window.addEventListener("unhandledrejection",function(e){' +
                                    'var r=e.reason;window.__showPreviewError("Unhandled promise rejection: "+(r&&r.message||r));' +
                                '});' +
                                'document.addEventListener("DOMContentLoaded",function(){' +
                                    'while(buf.length)window.__showPreviewError(buf.shift());' +
                                '});' +
                            '})();';

                        var bootstrapJs =
                            'document.addEventListener("DOMContentLoaded",function(){' +
                                'var b64=document.documentElement.getAttribute("data-jsx-source");' +
                                'var presets=(document.documentElement.getAttribute("data-jsx-presets")||"react").split(",");' +
                                'var src;try{src=decodeURIComponent(escape(atob(b64)));}' +
                                'catch(e){window.__showPreviewError("Failed to decode source: "+e.message);return;}' +
                                'if(typeof Babel==="undefined"){window.__showPreviewError("Babel did not load (check network access to unpkg.com).");return;}' +
                                'if(typeof React==="undefined"||typeof ReactDOM==="undefined"){window.__showPreviewError("React or ReactDOM did not load.");return;}' +
                                'var jsCode;try{jsCode=Babel.transform(src,{presets:presets}).code;}' +
                                'catch(e){var loc=e.loc?" (line "+e.loc.line+", col "+e.loc.column+")":"";' +
                                    'window.__showPreviewError("JSX/Babel parse: "+(e.message||e)+loc);return;}' +
                                'var fn;try{fn=new Function(jsCode+"\\n;return (window.__default_export||(typeof App!==\\"undefined\\"?App:null)||(typeof Component!==\\"undefined\\"?Component:null));");}' +
                                'catch(e){window.__showPreviewError("Compile: "+(e.message||e));return;}' +
                                'var c;try{c=fn();}' +
                                'catch(e){window.__showPreviewError("Script error: "+(e.message||e)+(e.stack?"\\n\\n"+e.stack:""));return;}' +
                                'if(!c){window.__showPreviewError("No exported component found. Define `export default ...` or a top-level function `App`/`Component`.");return;}' +
                                'try{ReactDOM.createRoot(document.getElementById("root")).render(React.createElement(c));}' +
                                'catch(e){window.__showPreviewError("Render: "+(e.message||e)+(e.stack?"\\n\\n"+e.stack:""));}' +
                            '});';

                        // Forward parent chat.html theme so the iframe's default
                        // scrollbars (and any system-default surfaces) match. Set
                        // at render time only; if the user later flips the theme,
                        // existing iframes keep their original color-scheme until
                        // re-rendered.
                        var parentTheme = (document.documentElement.getAttribute('data-theme') === 'dark') ? 'dark' : 'light';
                        var hostHtml =
                            '<!DOCTYPE html><html data-theme="' + parentTheme + '" data-jsx-source="' + transformedB64 + '" data-jsx-presets="' + presets + '">' +
                            '<head><meta charset="UTF-8"/>' +
                            '<meta name="viewport" content="width=device-width,initial-scale=1.0"/>' +
                            '<style>*,*::before,*::after{box-sizing:border-box}' +
                            'html{color-scheme:' + parentTheme + '}' +
                            'html,body{margin:0;background:' + (parentTheme === 'dark' ? '#1e1e1e' : '#ffffff') + ';font-family:system-ui,-apple-system,Segoe UI,sans-serif}' +
                            '#root{min-height:100vh}' +
                            '#__preview_error{padding:10px 14px;background:#2b1d1d;color:#ffb4b4;font:12px/1.5 ui-monospace,Consolas,monospace;white-space:pre-wrap;border-bottom:1px solid #5a2c2c}' +
                            SHADCN_CSS_VARS_CSS +
                            '</style>' +
                            '<script>' + errorInfraJs + '</script>' +
                            '<script src="https://unpkg.com/react@18.3.1/umd/react.development.js" crossorigin></script>' +
                            '<script src="https://unpkg.com/react-dom@18.3.1/umd/react-dom.development.js" crossorigin></script>' +
                            '<script>' + JSX_PRE_LIB_SHIM_JS + '</script>' +
                            '<script src="https://unpkg.com/@babel/standalone@7.29.0/babel.min.js" crossorigin></script>' +
                            '<script src="https://cdn.tailwindcss.com"></script>' +
                            '<script>' + SHADCN_TAILWIND_CONFIG_JS + '</script>' +
                            libScripts +
                            '<script>' + JSX_POST_LIB_SHIM_JS + '</script>' +
                            '<script>' + SHADCN_SHIM_JS + '</script>' +
                            '</head><body><div id="root"></div>' +
                            '<script>' + bootstrapJs + '</script>' +
                            '</body></html>';
                        var attrEscaped2 = hostHtml
                            .replace(/&/g, '&amp;')
                            .replace(/"/g, '&quot;')
                            .replace(/</g, '&lt;')
                            .replace(/>/g, '&gt;');
                        var sourceB64 = utf8ToBase64(code);
                        var jsxHl;
                        try { jsxHl = hljs.highlight(code, { language: lang }).value; }
                        catch(e) {
                            try { jsxHl = hljs.highlight(code, { language: 'javascript' }).value; }
                            catch(e2) { jsxHl = code.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
                        }
                        return '<div class="diagram-block" data-kind="jsx" data-view="preview" data-source-b64="' + sourceB64 + '">' +
                                previewHeader(lang, L2.jsxPreviewCopyTooltip || 'Copy source') +
                                '<div class="html-frame">' +
                                    '<iframe sandbox="allow-scripts" srcdoc="' + attrEscaped2 + '"></iframe>' +
                                '</div>' +
                                '<pre class="code-view"><code class="hljs language-' + lang + '">' + jsxHl + '</code></pre>' +
                               '</div>';
                    }

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
