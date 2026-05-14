using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading.Channels;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ChatPanel
{
    #region Public Methods (Execution)

    /// <summary>
    /// Adds a user message to the conversation without triggering the internal provider send flow.
    /// Use this when driving the conversation externally (e.g., via Anthropic SDK directly).
    /// </summary>
    /// <param name="text">The message text.</param>
    /// <param name="attachments">Optional attachments to include with the message.</param>
    /// <returns>The created user message.</returns>
    public async Task<ChatMessage> AddUserMessageAsync(string text, IReadOnlyList<ChatAttachment>? attachments = null)
    {
        SwitchToChatLayout();
        return await AddUserMessageCoreAsync(text, attachments ?? []);
    }

    /// <summary>
    /// Begins an external assistant turn. The caller drives the streaming via the returned handle.
    /// Only one handle may be active at a time; calling this while a previous handle is undisposed
    /// throws <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <param name="providerName">Display name of the provider (e.g., "Claude").</param>
    /// <param name="modelId">Model identifier (e.g., "claude-sonnet-4-6").</param>
    /// <param name="parentMessageId">Parent message ID for branching. Defaults to the last message.</param>
    /// <returns>An <see cref="AssistantTurnHandle"/> that must be disposed when the turn completes.</returns>
    public AssistantTurnHandle BeginAssistantTurn(
        string? providerName = null, string? modelId = null, string? parentMessageId = null)
    {
        if (_currentTurn is { IsDisposed: false })
            throw new InvalidOperationException("Previous assistant turn is not disposed. Dispose it before starting a new one.");

        var message = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = providerName,
            ProviderModelId = modelId,
            ParentId = parentMessageId ?? (_messages.Count > 0 ? _messages[^1].Id : null),
        };
        RegisterInTree(message);
        _messages.Add(message);
        _ = _renderer.BeginAssistantMessageAsync(message.Id, providerName, modelId);
        MessageAdded?.Invoke(this, message);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;

        // Create a CTS wired to the Stop button (OnStopRequested cancels _streamingCts)
        _streamingCts?.Dispose();
        _streamingCts = new CancellationTokenSource();

        var handle = new AssistantTurnHandle(this, message, _streamingCts.Token);
        _currentTurn = handle;
        return handle;
    }

    /// <summary>
    /// Sets keyboard focus to the message input text box.
    /// </summary>
    public void FocusInput() => _inputArea?.FocusInput();

    /// <summary>
    /// Pauses all playing audio and video elements. Called on tab switch.
    /// </summary>
    public void PauseAllMedia() => _ = _renderer.PauseAllMediaAsync();

    /// <summary>
    /// Creates a new WebView2 instance and inserts it into the chat layout grid.
    /// Called when a disposed ChatPanel is recycled by TabView for a new conversation tab.
    /// </summary>
    public async Task ReinitializeWebViewAsync()
    {
        if (_isInitialized || _initializing)
        {
            DiagnosticLogger.LogInfo("[Chat] ReinitializeWebView skipped: already initialized or in progress");
            return;
        }

        if (_chatLayout is null)
        {
            DiagnosticLogger.LogInfo("[Chat] ReinitializeWebView failed: _chatLayout is null");
            return;
        }

        DiagnosticLogger.LogInfo("[Chat] ReinitializeWebView: creating new WebView2 instance");
        _initializing = true;

        // Create a fresh WebView2 and insert at Row 0 of the chat layout grid
        _chatWebView = new WebView2
        {
            DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
            IsTabStop = false
        };
        Grid.SetRow(_chatWebView, 0);
        _chatLayout.Children.Insert(0, _chatWebView);

        // Run the same initialization as OnLoaded
        await _renderer.InitializeAsync(_chatWebView);
        _isInitialized = true;
        _needsWebViewReinitialization = false;
        await ApplyThemeAsync();
        await ApplyLocaleStringsAsync();
        ApplyChatZoom();
        if (IsDebugMode)
            await _renderer.SetDebugModeAsync(true);

        // Render any messages that were already restored before we got here
        await RenderRestoredMessagesAsync();

        DiagnosticLogger.LogInfo("[Chat] ReinitializeWebView: complete");
    }

    #endregion

    #region Message Sent Handler

    /// <summary>
    /// Handles the MessageSent event from the input area to send a user message and stream an assistant response.
    /// </summary>
    private async void OnMessageSent(object? sender, MessageSentEventArgs e)
    {
        if (!_isInitialized) return;
        if (string.IsNullOrWhiteSpace(e.Text) && e.Attachments.Count == 0) return;

        // Edit-mode confirm: create sibling branch instead of appending a new turn.
        if (IsEditingMessage)
        {
            await ConfirmEditAsync(e.Text, e.Attachments);
            return;
        }

        SwitchToChatLayout();

        var userMessage = await AddUserMessageCoreAsync(e.Text, e.Attachments);

        UserMessageSubmitted?.Invoke(this, e);

        // When external code drives the conversation, skip internal provider flow.
        if (DisableInternalSendFlow) return;

        // Stream assistant response
        if (Provider is null)
        {
            DiagnosticLogger.LogWarning("[Chat] Provider is null when sending message");
            var errorMsg = new ChatMessage(ChatRole.Assistant) { Content = "[Error: No AI provider configured]", ParentId = userMessage.Id };
            RegisterInTree(errorMsg);
            _messages.Add(errorMsg);
            await _renderer.BeginAssistantMessageAsync(errorMsg.Id, "Error", null);
            await _renderer.FinalizeMessageAsync(errorMsg.Id, errorMsg.Content);
            return;
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId,
            ParentId = userMessage.Id
        };
        RegisterInTree(assistantMessage);
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id, Provider.ProviderName, Provider.ModelId);
        MessageAdded?.Invoke(this, assistantMessage);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        await _renderer.SetStreamingAsync(true);
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        var elapsedSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            {
                var result = await StreamAndExecuteAsync(assistantMessage, ct);
                assistantMessage.TokenCount = result.Usage?.TotalTokens;
                assistantMessage.StopReason = result.IsTruncated ? StopReason.MaxTokens : StopReason.Completed;
                await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                    result.IsTruncated, result.Usage?.TotalTokens ?? 0,
                    elapsedSeconds: CaptureElapsed(elapsedSw, assistantMessage));
                DiagnosticLogger.LogInfo($"[Chat] Response complete — tokens={result.Usage?.TotalTokens ?? 0}, truncated={result.IsTruncated}, cache_write={result.Usage?.CacheCreationInputTokens ?? 0}, cache_read={result.Usage?.CacheReadInputTokens ?? 0}");
            }

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(userMessage.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);

            TryGenerateTitleAsync();

            // Auto-summarize if enabled and token threshold exceeded
            if (AutoSummarize && MaxInputTokens > 0 && Provider.LastUsage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await StreamSummaryAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogInfo("[Chat] Streaming cancelled by user");
            assistantMessage.StopReason = StopReason.UserCanceled;
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, assistantMessage),
                stopReason: assistantMessage.StopReason);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, assistantMessage));
        }
        finally
        {
            // CaptureElapsed already stopped the stopwatch and stored ElapsedSeconds
            // on the message; this is a defensive re-stop in case an early exit (e.g.,
            // an exception thrown before any FinalizeMessageAsync call) skipped it.
            if (elapsedSw.IsRunning) CaptureElapsed(elapsedSw, assistantMessage);
            assistantMessage.IsStreaming = false;
            await _renderer.SetStreamingAsync(false);
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
        }
    }

    #endregion

    #region Continue & Stop

    /// <summary>
    /// Stops the supplied stopwatch, writes the elapsed seconds onto the
    /// message, and returns the value so the caller can pass it through to
    /// <c>FinalizeMessageAsync</c>. Centralises the elapsed-capture step so
    /// cancel/error paths no longer skip it (which previously rendered as
    /// <c>0.0s</c> in the bubble footer).
    /// </summary>
    private static double CaptureElapsed(System.Diagnostics.Stopwatch sw, ChatMessage message)
    {
        sw.Stop();
        var seconds = sw.Elapsed.TotalSeconds;
        message.ElapsedSeconds = seconds;
        return seconds;
    }

    /// <summary>
    /// Handles the continue request from the renderer when the user clicks the
    /// Continue button on a truncated assistant response. Splits the next turn
    /// into its own bubble: a hidden "Continue writing…" user turn shapes the
    /// prompt without rendering, and a fresh assistant message (flagged as a
    /// continuation) hosts the new stream. The prior bubble is left untouched
    /// — no destructive innerHTML rewrite, no re-parse of already-rendered
    /// content, no JSX iframe reload.
    /// </summary>
    private async void OnContinueRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;
        DiagnosticLogger.LogInfo($"[Chat] Continue requested for message {messageId}");

        // Locate the assistant message the Continue button belongs to.
        var priorAssistant = _messages.LastOrDefault(m =>
            m.Role == ChatRole.Assistant && m.Id == messageId);
        if (priorAssistant is null) return;

        // Hidden user turn — sent to the provider as part of the prompt, but
        // skipped by the renderer (see ChatMessage.IsHidden). Persisted so the
        // tree round-trips through .astx without a phantom reload bubble.
        var continueMessage = new ChatMessage(ChatRole.User, "Continue writing from where you left off.")
        {
            ParentId = priorAssistant.Id,
            IsHidden = true,
        };
        RegisterInTree(continueMessage);
        _messages.Add(continueMessage);

        // Fresh assistant bubble for the new stream. IsContinuation drives the
        // small "↪ continued" label the renderer prepends so the new bubble
        // visually links back to priorAssistant.
        var continuationAssistant = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId,
            ParentId = continueMessage.Id,
            IsContinuation = true,
        };
        RegisterInTree(continuationAssistant);
        _messages.Add(continuationAssistant);

        await _renderer.BeginAssistantMessageAsync(
            continuationAssistant.Id,
            Provider.ProviderName,
            Provider.ModelId,
            isContinuation: true);
        MessageAdded?.Invoke(this, continuationAssistant);

        var elapsedSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (_inputArea is not null)
                _inputArea.IsInputEnabled = false;
            _streamingCts?.Cancel();
            _streamingCts = new CancellationTokenSource();
            var ct = _streamingCts.Token;

            var request = await CreateRequestAsync([.. _messages]);
            DiagnosticLogger.LogInfo($"[Chat] Continue → StreamAsync (messages={_messages.Count}, priorContentLen={priorAssistant.Content.Length})");

            var result = await ConsumeStreamAsync(Provider.StreamAsync(request, ct), continuationAssistant, ct);
            DiagnosticLogger.LogInfo($"[Chat] Continue complete — appended={continuationAssistant.Content.Length} chars, tokens={result.Usage?.TotalTokens ?? 0}, truncated={result.IsTruncated}");

            continuationAssistant.StopReason = result.IsTruncated ? StopReason.MaxTokens : StopReason.Completed;
            await _renderer.FinalizeMessageAsync(continuationAssistant.Id, continuationAssistant.Content,
                result.IsTruncated, result.Usage?.TotalTokens ?? 0,
                elapsedSeconds: CaptureElapsed(elapsedSw, continuationAssistant));

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(continueMessage.Id, Provider.LastRequestBody, continuationAssistant.Id, Provider.LastRawResponse);
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogInfo("[Chat] Continue cancelled by user");
            continuationAssistant.StopReason = StopReason.UserCanceled;
            await _renderer.FinalizeMessageAsync(continuationAssistant.Id, continuationAssistant.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, continuationAssistant),
                stopReason: continuationAssistant.StopReason);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            continuationAssistant.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(continuationAssistant.Id, continuationAssistant.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, continuationAssistant));
        }
        finally
        {
            if (elapsedSw.IsRunning) CaptureElapsed(elapsedSw, continuationAssistant);
            continuationAssistant.IsStreaming = false;

            // Mid-flight orphan guard. If the stream produced nothing — user
            // hit Stop before the first delta, network drop or 401/429 pre-
            // flight — drop both the hidden "Continue writing…" turn and the
            // empty assistant bubble so the tree round-trips clean. The
            // catch blocks above append "[Error: …]" on real exceptions, so
            // an empty Content here means "no answer attempted at all", which
            // is exactly the orphan case worth scrubbing. Anything non-empty
            // (real response, partial cancel, or "[Error]") is kept so the
            // reader can see what happened.
            if (string.IsNullOrEmpty(continuationAssistant.Content))
            {
                UnregisterFromTree(continuationAssistant);
                UnregisterFromTree(continueMessage);
                // Removes the empty bubble we created in BeginAssistantMessageAsync.
                // priorAssistant is still in the DOM, so this only sweeps the new pair.
                await _renderer.RemoveMessagesAfterAsync(priorAssistant.Id);
                DiagnosticLogger.LogInfo("[Chat] Continue produced no content — pruned hidden turn and empty assistant bubble");
            }

            await _renderer.SetStreamingAsync(false);
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
        }
    }

    /// <summary>
    /// Handles the stop button click from the input area to cancel the current streaming operation.
    /// </summary>
    private void OnStopRequested(object? sender, EventArgs e)
    {
        _streamingCts?.Cancel();
    }

    #endregion

    #region Render Command

    /// <summary>
    /// Batched rendering instruction sent from the background producer to the UI-thread consumer.
    /// </summary>
    private abstract record RenderCommand
    {
        /// <summary>Flush accumulated text tokens to the assistant message.</summary>
        public sealed record FlushText(string Text) : RenderCommand;

        /// <summary>Begin a collapsible thinking block (must precede FlushThinking).</summary>
        public sealed record BeginThinking : RenderCommand;

        /// <summary>Flush accumulated thinking tokens.</summary>
        public sealed record FlushThinking(string Text) : RenderCommand;

        /// <summary>Append assistant-generated media (e.g., Gemini inline image) below the message text.</summary>
        public sealed record AppendMedia(MediaContent Media) : RenderCommand;
    }

    #endregion

    #region Tool Result Utilities

    /// <summary>
    /// Inspects a tool result and returns a size-guarded version to prevent context overflow.
    /// Binary/base64 content is replaced with metadata; oversized text is truncated.
    /// Error results (short JSON with "error" key) pass through unchanged.
    /// </summary>
    private static string GuardToolResultSize(string toolResult, string toolName)
    {
        // Short results and error results pass through unchanged
        if (toolResult.Length <= MaxToolResultChars)
            return toolResult;

        if (toolResult.Length < 500 && toolResult.Contains("\"error\"", StringComparison.Ordinal))
            return toolResult;

        // Detect binary/base64 content — replace entirely with metadata
        if (IsBinaryContent(toolResult))
        {
            DiagnosticLogger.LogWarning(
                $"[Tool] Binary content detected: {toolName}, {toolResult.Length:N0} chars — replaced with metadata");
            return $"""
                [Binary/encoded content detected]
                Tool: {toolName}
                Original size: {toolResult.Length:N0} chars

                This result contains binary or base64-encoded content that cannot be used inline.
                Use DocumentParsers-integrated tools to extract text content, or read a specific section.
                """;
        }

        // Text content exceeding threshold — truncate with guidance
        DiagnosticLogger.LogWarning(
            $"[Tool] Result truncated: {toolName}, {toolResult.Length:N0} -> {MaxToolResultChars:N0} chars");
        return string.Concat(
            toolResult.AsSpan(0, MaxToolResultChars),
            $"\n\n--- truncated ---\nOriginal size: {toolResult.Length:N0} chars. Showing first {MaxToolResultChars:N0} chars.\nUse a more specific query or read a specific section.");
    }

    /// <summary>
    /// Detects whether the content is likely binary or base64-encoded.
    /// </summary>
    private static bool IsBinaryContent(ReadOnlySpan<char> content)
    {
        // Check for null characters (strong binary indicator)
        if (content.Contains('\0'))
            return true;

        // Check for base64 pattern: long string of [A-Za-z0-9+/=] with no newlines
        if (content.Length >= Base64DetectionThreshold)
        {
            // Sample the first portion for base64 characteristics
            var sample = content[..Math.Min(1000, content.Length)];
            var base64Chars = 0;
            foreach (var c in sample)
            {
                if (char.IsLetterOrDigit(c) || c is '+' or '/' or '=')
                    base64Chars++;
            }

            // If >90% of sampled chars are base64-alphabet, likely encoded
            if (base64Chars > sample.Length * 0.9)
                return true;
        }

        // Check control character ratio in a sample
        var ctrlSample = content[..Math.Min(2000, content.Length)];
        var controlChars = 0;
        foreach (var c in ctrlSample)
        {
            if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
                controlChars++;
        }

        return controlChars > ctrlSample.Length * 0.05; // >5% control chars
    }

    /// <summary>
    /// Logs structured summary for RAG tool results (search_documents, get_document_chunk).
    /// </summary>
    private static void LogStructuredToolResult(string toolName, string resultJson)
    {
        try
        {
            if (toolName == "search_documents")
            {
                using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
                var root = doc.RootElement;
                var mode = root.TryGetProperty("search_mode", out var m) ? m.GetString() : "?";
                var total = root.TryGetProperty("total_chunks_searched", out var t) ? t.GetInt32() : 0;
                var results = root.TryGetProperty("results", out var r) ? r : default;
                var count = results.ValueKind == System.Text.Json.JsonValueKind.Array ? results.GetArrayLength() : 0;

                DiagnosticLogger.LogInfo(
                    $"[Tool] Result: search_documents → mode={mode}, {count} results, {total} chunks");

                if (results.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var i = 1;
                    foreach (var item in results.EnumerateArray())
                    {
                        var src = item.TryGetProperty("source_path", out var s)
                            ? System.IO.Path.GetFileName(s.GetString() ?? "") : "?";
                        var ci = item.TryGetProperty("chunk_index", out var c) ? c.GetInt32() : -1;
                        var score = item.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0;
                        DiagnosticLogger.LogInfo($"  #{i} {src} [chunk {ci}] score={score:F3}");
                        i++;
                    }
                }
            }
            else if (toolName == "get_document_chunk")
            {
                using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
                var root = doc.RootElement;
                var chunkId = root.TryGetProperty("chunk_id", out var id) ? id.ToString() : "?";
                var src = root.TryGetProperty("source_path", out var s)
                    ? System.IO.Path.GetFileName(s.GetString() ?? "") : "?";
                var ci = root.TryGetProperty("chunk_index", out var c) ? c.GetInt32() : -1;
                DiagnosticLogger.LogInfo(
                    $"[Tool] Result: get_document_chunk → id={chunkId}, {src} [chunk {ci}]");
            }
        }
        catch
        {
            // JSON parsing failure — the existing length log is sufficient
        }
    }

    /// <summary>
    /// Extracts a renderable inline chart from a tool result's <c>structuredContent</c>.
    /// <para/>
    /// Looks for a <c>chart</c> object with <c>type == "plotly"</c> — the shape
    /// shipped by tools like <c>ls_get_chart</c>. Called at render time on both
    /// the live tool-execution path and the conversation-restore path; the raw
    /// <c>structuredContent</c> itself is what gets persisted (on
    /// <see cref="ChatMessage.StructuredContent"/>), this just picks the chart
    /// node out of it. The returned element is cloned (<c>JsonElement.Clone</c>)
    /// so it is independent of the input's backing
    /// <see cref="System.Text.Json.JsonDocument"/>.
    /// </summary>
    /// <param name="structuredContent">The tool result's structured content, if any.</param>
    /// <returns>The cloned <c>chart</c> JSON object, or <see langword="null"/> when absent or not a recognized chart type.</returns>
    private static System.Text.Json.JsonElement? TryExtractInlineChart(System.Text.Json.JsonElement? structuredContent)
    {
        if (structuredContent is not { } root ||
            root.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        if (!root.TryGetProperty("chart", out var chart) ||
            chart.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        // Only "plotly" charts are renderable today. An unknown discriminator is
        // ignored rather than rendered, so a future chart type a server adds does
        // not surface as a broken block in an older client.
        if (!chart.TryGetProperty("type", out var type) ||
            type.ValueKind != System.Text.Json.JsonValueKind.String ||
            !string.Equals(type.GetString(), "plotly", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return chart.Clone();
    }

    /// <summary>
    /// Formats a rich label for fetch_url tool blocks: "fetch_url("url") — N chars".
    /// Falls back to plain "fetch_url" on parse failure.
    /// </summary>
    private static string FormatFetchUrlLabel(string argumentsJson, int resultLength)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("url", out var urlProp))
            {
                var url = urlProp.GetString() ?? "?";
                return $"fetch_url(\u201C{url}\u201D) \u2014 {resultLength:N0} chars";
            }
        }
        catch { /* fall through */ }
        return "fetch_url";
    }

    /// <summary>
    /// Checks if a delegate_task call targets a registered specialist.
    /// Only returns true if the specialist name is validated by
    /// <see cref="IsRegisteredSpecialist"/>, preventing AI from bypassing
    /// approval by injecting arbitrary specialist names.
    /// </summary>
    private bool IsRegisteredSpecialistCall(string argumentsJson)
    {
        var name = GetSpecialistName(argumentsJson);
        return name is not null && IsRegisteredSpecialist?.Invoke(name) == true;
    }

    /// <summary>
    /// Checks if a delegate_task call has a specialist field (for UI labeling).
    /// </summary>
    private static string? GetSpecialistName(string argumentsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("specialist", out var sp))
                return sp.GetString();
        }
        catch { /* fall through */ }
        return null;
    }

    /// <summary>
    /// Gets the display name for a specialist, or null if not found.
    /// </summary>
    private string? GetSpecialistDisplayName(string specialistName)
        => SpecialistDisplayNameResolver?.Invoke(specialistName);

    /// <summary>Formats a display label for a specialist tool call, including prompt snippet and status icon.</summary>
    private string FormatSpecialistLabel(string argumentsJson, string resultJson)
    {
        var specialistName = GetSpecialistName(argumentsJson);
        var displayName = specialistName is not null
            ? GetSpecialistDisplayName(specialistName) ?? "Specialist"
            : "Specialist";

        var promptSnippet = displayName;
        var statusIcon = "\u2713"; // ✓ default

        try
        {
            using var argsDoc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (argsDoc.RootElement.TryGetProperty("prompt", out var promptProp))
            {
                var prompt = promptProp.GetString() ?? "";
                promptSnippet = prompt.Length > 40
                    ? $"{displayName}: {prompt[..40]}\u2026"
                    : $"{displayName}: {prompt}";
            }
        }
        catch { /* fall through */ }

        try
        {
            using var resultDoc = System.Text.Json.JsonDocument.Parse(resultJson);
            if (resultDoc.RootElement.TryGetProperty("status", out var statusProp))
            {
                statusIcon = statusProp.GetString() switch
                {
                    "completed" => "\u2713",           // ✓
                    "failed" => "\u2717",               // ✗
                    "timed_out" => "\u23F1",            // ⏱
                    "max_rounds_reached" => "\u26A0",   // ⚠
                    _ => "\u2713",
                };
            }
        }
        catch { /* fall through */ }

        return $"{promptSnippet} {statusIcon}";
    }

    /// <summary>Formats a display label for a sub-agent tool call, including prompt snippet and status icon.</summary>
    private static string FormatSubAgentLabel(string argumentsJson, string resultJson)
    {
        // Extract prompt (truncated) and status from sub-agent call
        var promptSnippet = "Sub-Agent";
        var statusIcon = "\u2713"; // ✓ default

        try
        {
            using var argsDoc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (argsDoc.RootElement.TryGetProperty("prompt", out var promptProp))
            {
                var prompt = promptProp.GetString() ?? "";
                promptSnippet = prompt.Length > 40
                    ? $"Sub-Agent: {prompt[..40]}\u2026"
                    : $"Sub-Agent: {prompt}";
            }
        }
        catch { /* fall through */ }

        try
        {
            using var resultDoc = System.Text.Json.JsonDocument.Parse(resultJson);
            if (resultDoc.RootElement.TryGetProperty("status", out var statusProp))
            {
                statusIcon = statusProp.GetString() switch
                {
                    "completed" => "\u2713",           // ✓
                    "failed" => "\u2717",               // ✗
                    "timed_out" => "\u23F1",            // ⏱
                    "max_rounds_reached" => "\u26A0",   // ⚠
                    _ => "\u2713",
                };
            }
        }
        catch { /* fall through */ }

        return $"{promptSnippet} {statusIcon}";
    }

    #endregion

    #region Active Tools

    /// <summary>
    /// Threshold in bytes for converting data URI media to temporary files.
    /// Data URIs larger than this are saved to disk and replaced with file:// URIs.
    /// </summary>
    private const int LargeMediaThreshold = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Returns the currently active tools, filtered by the input area's enabled tool selection.
    /// Tool policy (search_tools substitution, etc.) is applied at the App layer (ResolveTools).
    /// </summary>
    private IReadOnlyList<IAssistTool> GetActiveTools()
    {
        var enabledNames = _inputArea?.EnabledToolNames;
        return enabledNames is null
            ? RegisteredTools
            : [.. RegisteredTools.Where(t => enabledNames.Contains(t.Name))];
    }

    #endregion

    #region Stream Consumer

    // StreamResult is now a public type in Models/StreamResult.cs

    /// <summary>
    /// Internal entry point for <see cref="AssistantTurnHandle.ConsumeStreamAsync"/>.
    /// </summary>
    internal Task<StreamResult> ConsumeStreamInternalAsync(
        IAsyncEnumerable<StreamEvent> events, ChatMessage message, CancellationToken ct)
        => ConsumeStreamAsync(events, message, ct);

    /// <summary>
    /// Finalizes an externally-driven assistant turn: renders the final message state
    /// and restores the input area.
    /// </summary>
    internal async Task FinalizeHandleAsync(ChatMessage message)
    {
        message.IsStreaming = false;
        await _renderer.FinalizeMessageAsync(message.Id, message.Content);
        if (_inputArea is not null)
        {
            _inputArea.IsInputEnabled = true;
            _inputArea.FocusInput();
        }
        if (_currentTurn?.Message == message)
            _currentTurn = null;
    }

    /// <summary>
    /// Consumes a stream of <see cref="StreamEvent"/> instances on a background thread,
    /// forwarding batched rendering commands to the UI thread via a <see cref="Channel{T}"/>.
    /// Returns aggregated usage, truncation info, and any tool calls.
    /// </summary>
    private async Task<StreamResult> ConsumeStreamAsync(
        IAsyncEnumerable<StreamEvent> events, ChatMessage message, CancellationToken ct)
    {
        TokenUsage? usage = null;
        var isTruncated = false;
        var toolAccumulator = new StreamToolCallAccumulator();
        var thinkingBlockStarted = false;

        var channel = Channel.CreateUnbounded<RenderCommand>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        // ── Producer (background thread) ──────────────────────────────
        var producer = Task.Run(async () =>
        {
            try
            {
                var textBatch = new System.Text.StringBuilder();
                var thinkingBatch = new System.Text.StringBuilder();
                var lastFlush = Environment.TickCount64;

                await foreach (var evt in events.WithCancellation(ct))
                {
                    switch (evt)
                    {
                        case StreamEvent.ThinkingDelta thinking:
                            if (!thinkingBlockStarted)
                            {
                                if (textBatch.Length > 0)
                                {
                                    channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));
                                    textBatch.Clear();
                                }
                                channel.Writer.TryWrite(new RenderCommand.BeginThinking());
                                thinkingBlockStarted = true;
                            }
                            thinkingBatch.Append(thinking.Text);
                            break;

                        case StreamEvent.TextDelta delta:
                            textBatch.Append(delta.Text);
                            break;

                        case StreamEvent.ToolCallStart start:
                            toolAccumulator.HandleStart(start);
                            break;

                        case StreamEvent.ToolCallDelta delta:
                            toolAccumulator.HandleDelta(delta);
                            break;

                        case StreamEvent.MediaPart media:
                            // Flush any pending text first so the media renders below the text-so-far.
                            if (textBatch.Length > 0)
                            {
                                channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));
                                textBatch.Clear();
                            }
                            channel.Writer.TryWrite(new RenderCommand.AppendMedia(media.Media));
                            break;

                        case StreamEvent.Usage u:
                            usage = u.TokenUsage;
                            break;

                        case StreamEvent.StreamCompleted completed:
                            isTruncated = completed.IsTruncated;
                            break;
                    }

                    var now = Environment.TickCount64;
                    if (now - lastFlush >= 50)
                    {
                        if (thinkingBatch.Length > 0)
                        {
                            channel.Writer.TryWrite(new RenderCommand.FlushThinking(thinkingBatch.ToString()));
                            thinkingBatch.Clear();
                        }
                        if (textBatch.Length > 0)
                        {
                            channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));
                            textBatch.Clear();
                        }
                        lastFlush = now;
                    }
                }

                // Final flush
                if (thinkingBatch.Length > 0)
                    channel.Writer.TryWrite(new RenderCommand.FlushThinking(thinkingBatch.ToString()));
                if (textBatch.Length > 0)
                    channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
                throw;
            }
        }, ct);

        // ── Consumer (UI thread) ──────────────────────────────────────
        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var cmd))
                {
                    switch (cmd)
                    {
                        case RenderCommand.FlushText ft:
                            message.Content += ft.Text;
                            await _renderer.AppendTokenAsync(message.Id, ft.Text);
                            break;

                        case RenderCommand.BeginThinking:
                            await _renderer.BeginThinkingBlockAsync(message.Id);
                            break;

                        case RenderCommand.FlushThinking ft:
                            message.ThinkingContent = (message.ThinkingContent ?? "") + ft.Text;
                            await _renderer.AppendThinkingTokenAsync(message.Id, ft.Text);
                            break;

                        case RenderCommand.AppendMedia am:
                            // Persist on the message so save/load can restore the image,
                            // then render via the same pipeline used for tool-result media.
                            message.AppendToolMedia(am.Media);
                            await _renderer.AppendToolMediaAsync(message.Id, am.Media);
                            break;
                    }
                }

                await Task.Delay(50, ct);
            }
        }
        catch (ChannelClosedException)
        {
            // Producer exception — will be surfaced by await producer below
        }

        // Propagate producer exceptions (HTTP errors, parsing failures, etc.)
        await producer;

        if (thinkingBlockStarted)
            await _renderer.EndThinkingBlockAsync(message.Id);

        var toolCalls = toolAccumulator.HasToolCalls ? toolAccumulator.Drain() : null;
        return new StreamResult(usage, isTruncated, toolCalls);
    }

    #endregion

    #region Stream And Execute

    /// <summary>
    /// Streams an AI response and executes any tool calls, looping until the AI
    /// produces a text-only response or max rounds are reached. Unifies the previously
    /// separate streaming-only and tool-calling code paths.
    /// </summary>
    private async Task<StreamResult> StreamAndExecuteAsync(
        ChatMessage assistantMessage, CancellationToken ct)
    {
        ToolCallExecutor? executor = null;
        var activeTools = GetActiveTools();

        // Auto-connect servers and filter to connected tools before sending
        if (PrepareToolsForSendAsync is not null)
            activeTools = await PrepareToolsForSendAsync(activeTools);

        if (activeTools.Count > 0)
        {
            // Read McpTools after delegate (it may have updated connection-filtered tools)
            var executableTools = McpTools is { Count: > 0 } mcpTools
                ? [.. activeTools, .. mcpTools]
                : activeTools;
            executor = new ToolCallExecutor(executableTools);
            // Fallback: resolve tools discovered via search_tools from McpTools at runtime
            executor.FallbackToolResolver = name =>
                McpTools.FirstOrDefault(t => t.Name == name);
            if (_approvalPanel is not null && _inputArea is not null)
            {
                executor.ConfirmationHandler = async (toolName, arguments) =>
                {
                    // Auto-approve specialist calls — validated against SpecialistRegistry
                    if (toolName == "delegate_task" && IsRegisteredSpecialistCall(arguments))
                    {
                        DiagnosticLogger.LogInfo($"[Tool] Specialist auto-approved: {toolName}");
                        return (true, null);
                    }

                    DiagnosticLogger.LogInfo($"[Tool] Approval requested: {toolName}");
                    var matchedTool = RegisteredTools.FirstOrDefault(t => t.Name == toolName)
                        ?? McpTools.FirstOrDefault(t => t.Name == toolName);
                    _approvalPanel.ToolName = toolName;
                    _approvalPanel.ToolDisplayName = GetToolDisplayName(toolName);
                    _approvalPanel.ServerName = (matchedTool as McpToolAdapter)?.ServerName ?? "";
                    _approvalPanel.Arguments = arguments;
                    _approvalPanel.IsExpanded = true;
                    _inputArea.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _approvalPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    _approvalPanel.FocusUserNote();

                    _approvalTcs = new TaskCompletionSource<(bool, string?)>();
                    var (approved, userNote) = await _approvalTcs.Task;
                    DiagnosticLogger.LogInfo($"[Tool] Approval result: {toolName} → {(approved ? "approved" : "rejected")}{(userNote is not null ? $" (note: {userNote})" : "")}");

                    _approvalPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _inputArea.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                    return (approved, userNote);
                };
            }
        }

        StreamResult result;
        var round = 0;
        string? pendingUserNote = null;
        var roundStartIndex = assistantMessage.Content?.Length ?? 0;

        do
        {
            // Inject user note from previous tool approval as a transient user message
            // (not persisted in _messages — only visible to the LLM in this API call)
            IReadOnlyList<ChatMessage> messages = pendingUserNote is not null
                ? [.. _messages, new ChatMessage(ChatRole.User, pendingUserNote)]
                : [.. _messages];
            pendingUserNote = null;
            var request = await CreateRequestAsync(messages, activeTools);
            if (round == 0)
            {
                DiagnosticLogger.LogInfo($"[Chat] Request start — provider={Provider!.ProviderName}, model={Provider.ModelId}, tools={activeTools.Count}, thinking={request.ThinkingEnabled}");
                DiagnosticLogger.LogInfo($"[Chat][Debug] activeTools=[{string.Join(",", activeTools.Select(t => t.Name))}]");
                DiagnosticLogger.LogInfo($"[Chat][Debug] mcpTools=[{string.Join(",", (McpTools ?? []).Select(t => t.Name))}]");
            }
            result = await ConsumeStreamAsync(Provider!.StreamAsync(request, ct), assistantMessage, ct);
            DiagnosticLogger.LogInfo($"[Chat] Stream completed — tokens={result.Usage?.TotalTokens ?? 0}, truncated={result.IsTruncated}, hasToolCalls={result.HasToolCalls}, cache_write={result.Usage?.CacheCreationInputTokens ?? 0}, cache_read={result.Usage?.CacheReadInputTokens ?? 0}");

            if (!result.HasToolCalls || executor is null)
                break;

            round++;
            var delegateCount = result.ToolCalls!.Count(tc => tc.FunctionName == "delegate_task");
            DiagnosticLogger.LogInfo($"[Chat] Tool round {round}/{MaxToolCallRounds}, toolCalls={result.ToolCalls!.Count}"
                + (delegateCount > 1 ? $", delegate_task×{delegateCount} (parallel candidate)" : ""));
            if (round > MaxToolCallRounds)
            {
                DiagnosticLogger.LogWarning($"[Tool] Max tool call rounds ({MaxToolCallRounds}) exceeded, stopping");
                break;
            }

            // Add the assistant's tool call message to history (delta content only)
            var toolCallParentId = _messages.Count > 0 ? _messages[^1].Id : null;
            var deltaContent = (assistantMessage.Content?.Length > roundStartIndex)
                ? assistantMessage.Content[roundStartIndex..]
                : "";
            var toolCallMsg = new ChatMessage(ChatRole.Assistant)
            {
                ToolCalls = result.ToolCalls,
                Content = deltaContent,
                ProviderName = Provider.ProviderName,
                ProviderModelId = Provider.ModelId,
                ParentId = toolCallParentId
            };
            RegisterInTree(toolCallMsg);
            _messages.Add(toolCallMsg);

            // Split tool calls into regular and sub-agent groups
            var subAgentCalls = result.ToolCalls!.Where(tc => tc.FunctionName == "delegate_task").ToList();
            var otherCalls = result.ToolCalls!.Where(tc => tc.FunctionName != "delegate_task").ToList();

            // Phase 1: Regular tools — sequential execution (existing behavior)
            foreach (var call in otherCalls)
            {
                DiagnosticLogger.LogInfo($"[Tool] Executing: {call.FunctionName} (id={call.Id})");
                ToolExecutionResult execResult;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var isError = false;
                try
                {
                    execResult = await executor.ExecuteAsync(call, ct);
                    DiagnosticLogger.LogInfo($"[Tool] Result: {call.FunctionName}, length={execResult.Text.Length}");
                    LogStructuredToolResult(call.FunctionName, execResult.Text);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // User pressed STOP during tool execution — not an error.
                    DiagnosticLogger.LogInfo($"[Tool] Cancelled by user: {call.FunctionName}");
                    execResult = new ToolExecutionResult(
                        System.Text.Json.JsonSerializer.Serialize(new { error = "The operation was canceled." }));
                    isError = true;
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogWarning($"[Tool] Execution error: {call.FunctionName} — {ex.Message}");
                    DiagnosticLogger.LogException(ex);
                    execResult = new ToolExecutionResult(
                        System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
                    isError = true;
                }
                sw.Stop();

                if (executor.LastUserNote is not null)
                    pendingUserNote = executor.LastUserNote;

                var toolResult = GuardToolResultSize(execResult.Text, call.FunctionName);
                // Clone so the persisted element stays valid after the originating
                // JsonDocument (owned by the MCP SDK's CallToolResult) is released.
                var structuredContent = execResult.StructuredContent?.Clone();

                var toolResultMsg = new ChatMessage(ChatRole.Tool, toolResult)
                {
                    ToolCallId = call.Id,
                    ParentId = toolCallMsg.Id,
                    ToolMedia = execResult.MediaContents,
                    StructuredContent = structuredContent
                };
                RegisterInTree(toolResultMsg);
                _messages.Add(toolResultMsg);

                if (call.FunctionName == "search_documents")
                    await _renderer.AppendSearchResultBlockAsync(assistantMessage.Id, toolResult, call.FunctionName);
                else
                    await _renderer.AppendToolBlockAsync(
                        assistantMessage.Id, call.FunctionName, call.Arguments,
                        toolResult, sw.ElapsedMilliseconds, isError);

                // Inline chart shipped via structuredContent (e.g. a Plotly spec from
                // ls_get_chart). Rendered as its own block below the collapsible tool
                // block — independent of tool name, and zero token cost since the spec
                // never entered the model's text context.
                if (TryExtractInlineChart(structuredContent) is { } chartElement)
                    await _renderer.AppendChartBlockAsync(
                        assistantMessage.Id, chartElement.GetRawText());

                if (execResult.MediaContents is { Count: > 0 } mediaItems)
                {
                    foreach (var media in mediaItems)
                        await _renderer.AppendToolMediaAsync(
                            assistantMessage.Id, ConvertLargeMediaToTempFile(media));
                }
            }

            // Phase 2: Sub-Agent calls — approve sequentially, execute in parallel
            if (subAgentCalls.Count > 0)
            {
                var approved = new List<(ToolCall Call, string? UserNote, Task<ToolExecutionResult> Task)>();
                var rejected = new List<(ToolCall Call, string RejectionText)>();
                // IDs of pending tool blocks that have been rendered but not yet
                // resolved. Each successful Resolve in 2c removes itself. Anything
                // left when control exits — via cancellation, exception, or partial
                // completion — is swept by the finally below with an "[interrupted]"
                // marker so the DOM never keeps a pulse going forever.
                var pendingCallIds = new List<string>();

                try
                {
                // 2a: Sequential approval, immediate execution start.
                // For each approved call we render a pending tool block *before*
                // awaiting so the user sees a pulsing placeholder instead of a
                // silent UI while sub-agents run (often tens of seconds).
                foreach (var call in subAgentCalls)
                {
                    DiagnosticLogger.LogInfo($"[Tool] Sub-Agent approval: {call.FunctionName} (id={call.Id})");

                    if (executor.ConfirmationHandler is not null)
                    {
                        var (isApproved, userNote) = await executor.ConfirmationHandler(call.FunctionName, call.Arguments);
                        if (isApproved)
                        {
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent approved, starting parallel: {call.Id}");
                            await _renderer.BeginToolBlockAsync(
                                assistantMessage.Id, call.Id, call.FunctionName, call.Arguments);
                            pendingCallIds.Add(call.Id);
                            var task = executor.ExecuteWithoutConfirmationAsync(call, userNote, ct);
                            approved.Add((call, userNote, task));
                        }
                        else
                        {
                            var reason = string.IsNullOrWhiteSpace(userNote)
                                ? "Tool call rejected by user."
                                : $"Tool call rejected by user. Reason: {userNote}";
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent rejected: {call.Id} — {reason}");
                            rejected.Add((call, reason));
                        }
                    }
                    else
                    {
                        await _renderer.BeginToolBlockAsync(
                            assistantMessage.Id, call.Id, call.FunctionName, call.Arguments);
                        pendingCallIds.Add(call.Id);
                        var task = executor.ExecuteWithoutConfirmationAsync(call, null, ct);
                        approved.Add((call, null, task));
                    }
                }

                // 2b: Wait for all parallel executions to complete
                if (approved.Count > 0)
                {
                    DiagnosticLogger.LogInfo($"[Tool] Awaiting {approved.Count} parallel sub-agent tasks");
                    try { await Task.WhenAll(approved.Select(a => a.Task)); }
                    catch { /* individual errors handled in 2c */ }
                }

                // 2c: Collect results in original call order
                foreach (var call in subAgentCalls)
                {
                    ToolExecutionResult execResult;
                    string? noteForPending = null;
                    var isError = false;

                    var approvedEntry = approved.FirstOrDefault(a => a.Call.Id == call.Id);
                    if (approvedEntry.Task is not null)
                    {
                        try
                        {
                            execResult = await approvedEntry.Task;
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent result: {call.Id}, length={execResult.Text.Length}");
                            LogStructuredToolResult(call.FunctionName, execResult.Text);
                            noteForPending = approvedEntry.UserNote;
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested)
                        {
                            // User pressed STOP — not an error, no stack trace needed.
                            // Distinguish from sub-agent internal timeout (where ct stays uncancelled)
                            // and from any other unexpected cancellation.
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent cancelled by user: {call.Id}");
                            execResult = new ToolExecutionResult(
                                System.Text.Json.JsonSerializer.Serialize(new { error = "The operation was canceled." }));
                            isError = true;
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLogger.LogWarning($"[Tool] Sub-Agent error: {call.Id} — {ex.Message}");
                            DiagnosticLogger.LogException(ex);
                            execResult = new ToolExecutionResult(
                                System.Text.Json.JsonSerializer.Serialize(new { error = ex.Message }));
                            isError = true;
                        }
                    }
                    else
                    {
                        var (Call, RejectionText) = rejected.First(r => r.Call.Id == call.Id);
                        execResult = new ToolExecutionResult(RejectionText);
                        isError = true;
                    }

                    if (noteForPending is not null)
                        pendingUserNote = noteForPending;

                    var toolResult = GuardToolResultSize(execResult.Text, call.FunctionName);
                    var toolResultMsg = new ChatMessage(ChatRole.Tool, toolResult)
                    {
                        ToolCallId = call.Id,
                        ParentId = toolCallMsg.Id
                    };
                    RegisterInTree(toolResultMsg);
                    _messages.Add(toolResultMsg);

                    // For approved calls a pending block was rendered in 2a — resolve
                    // it in-place. For rejected calls no pending block exists (we skip
                    // BeginToolBlock on rejection), so append the rejection result via
                    // the finished-block path; resolveToolBlock falls back to
                    // appendToolBlock when no pending block matches callId, so calling
                    // the resolve API is safe either way.
                    await _renderer.ResolveToolBlockAsync(
                        assistantMessage.Id, call.Id, call.FunctionName, call.Arguments,
                        toolResult, null, isError);
                    pendingCallIds.Remove(call.Id);

                    if (execResult.MediaContents is { Count: > 0 } mediaItems)
                    {
                        foreach (var media in mediaItems)
                            await _renderer.AppendToolMediaAsync(assistantMessage.Id, media);
                    }
                }
                }
                finally
                {
                    // Sweep any pending blocks that never reached a normal Resolve.
                    // Reasons: user hit Stop mid-flight, an exception escaped 2a/2b/2c,
                    // or the 2c loop aborted between iterations. Inner try/catch is
                    // required because the renderer may already be torn down during
                    // shutdown paths and we don't want this cleanup to itself throw.
                    foreach (var callId in pendingCallIds)
                    {
                        try
                        {
                            await _renderer.ResolveToolBlockAsync(
                                assistantMessage.Id, callId, "[interrupted]",
                                arguments: null, result: null,
                                durationMs: null, isError: true);
                        }
                        catch (Exception sweepEx)
                        {
                            DiagnosticLogger.LogWarning(
                                $"[Tool] Pending block sweep failed for {callId}: {sweepEx.Message}");
                        }
                    }
                }
            }
            roundStartIndex = assistantMessage.Content?.Length ?? 0;
        } while (true);

        return result;
    }

    #endregion

    #region Assistant Response Streaming

    /// <summary>
    /// Streams a new assistant response for the given user message using the configured provider.
    /// </summary>
    private async Task StreamAssistantResponseAsync(ChatMessage userMessage)
    {
        if (Provider is null) return;

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId,
            ParentId = userMessage.Id
        };
        RegisterInTree(assistantMessage);
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id, Provider.ProviderName, Provider.ModelId);
        MessageAdded?.Invoke(this, assistantMessage);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        await _renderer.SetStreamingAsync(true);
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        var elapsedSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await StreamAndExecuteAsync(assistantMessage, ct);
            assistantMessage.TokenCount = result.Usage?.TotalTokens;
            assistantMessage.StopReason = result.IsTruncated ? StopReason.MaxTokens : StopReason.Completed;

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                result.IsTruncated, result.Usage?.TotalTokens ?? 0,
                elapsedSeconds: CaptureElapsed(elapsedSw, assistantMessage));

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(userMessage.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);

            if (AutoSummarize && MaxInputTokens > 0 && result.Usage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await StreamSummaryAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogInfo("[Chat] Streaming cancelled by user");
            assistantMessage.StopReason = StopReason.UserCanceled;
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, assistantMessage),
                stopReason: assistantMessage.StopReason);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, assistantMessage));
        }
        finally
        {
            if (elapsedSw.IsRunning) CaptureElapsed(elapsedSw, assistantMessage);
            assistantMessage.IsStreaming = false;
            await _renderer.SetStreamingAsync(false);
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
        }
    }

    /// <summary>
    /// Streams a conversation summary as a new assistant message with <see cref="SummaryMeta"/>.
    /// Finds the range since the last summary (or conversation start), builds a temporary
    /// summarization request, and streams the result. Original messages are preserved —
    /// prompt truncation is handled by <see cref="ApplySummaryTruncation"/> at build time.
    /// </summary>
    private async Task StreamSummaryAsync(CancellationToken ct)
    {
        if (Provider is null) return;

        var summaryProvider = AuxiliaryProviderResolver is { } resolver
            ? await resolver.ResolveWithFallbackAsync(SummaryModel, Provider, "Summary", ct)
            : Provider;

        // Find the range to summarize: from last summary (exclusive) to end of _messages
        var startIndex = 0;
        for (var i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].Summary is not null)
            {
                startIndex = i + 1;
                break;
            }
        }

        var coveredMessages = _messages.Skip(startIndex).ToList();
        if (coveredMessages.Count == 0) return;

        DiagnosticLogger.LogInfo($"[Chat] StreamSummary — covering {coveredMessages.Count} messages from index {startIndex}");

        // Build summarization request (transient — not stored anywhere)
        var historyText = string.Join("\n",
            coveredMessages.Select(m => $"{m.Role}: {m.Content}"));

        // If there's a previous summary, include it for cumulative context
        var previousSummary = startIndex > 0 ? _messages[startIndex - 1] : null;
        var prompt = previousSummary is not null
            ? $"Previous summary:\n{previousSummary.Content}\n\nNew conversation to integrate into the summary:\n\n{historyText}"
            : $"Summarize the following conversation concisely, preserving key context and decisions:\n\n{historyText}";

        var summaryRequest = new AiRequest
        {
            Messages = [new ChatMessage(ChatRole.User, prompt)],
            SystemPrompt = "You are a helpful assistant that creates concise conversation summaries.\nRespond in the same language as the conversation being summarized."
        };

        // Create summary node: ParentId = last assistant message
        var lastAssistant = _messages.LastOrDefault(m => m.Role == ChatRole.Assistant);
        var summaryMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = summaryProvider.ProviderName,
            ProviderModelId = summaryProvider.ModelId,
            ParentId = lastAssistant?.Id,
            Summary = new SummaryMeta
            {
                CoveredMessageIds = [.. coveredMessages.Select(m => m.Id)]
            }
        };
        RegisterInTree(summaryMessage);
        _messages.Add(summaryMessage);
        await _renderer.BeginAssistantMessageAsync(summaryMessage.Id, summaryProvider.ProviderName, summaryProvider.ModelId, isSummary: true);

        var elapsedSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await ConsumeStreamAsync(summaryProvider.StreamAsync(summaryRequest, ct), summaryMessage, ct);

            // Store token counts for compression ratio display
            if (result.Usage is { } u)
            {
                summaryMessage.Summary!.CoveredTokenCount = u.InputTokens;
                summaryMessage.TokenCount = u.OutputTokens;
            }

            var coveredTokens = result.Usage?.InputTokens ?? 0;
            var summaryTokens = result.Usage?.OutputTokens ?? 0;
            summaryMessage.StopReason = result.IsTruncated ? StopReason.MaxTokens : StopReason.Completed;
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content, result.IsTruncated,
                tokenCount: summaryTokens, coveredTokenCount: coveredTokens,
                elapsedSeconds: CaptureElapsed(elapsedSw, summaryMessage));
            DiagnosticLogger.LogInfo($"[Chat] StreamSummary complete — covered {coveredMessages.Count} messages, tokens {coveredTokens} → {summaryTokens}");
        }
        catch (OperationCanceledException)
        {
            summaryMessage.StopReason = StopReason.UserCanceled;
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, summaryMessage),
                stopReason: summaryMessage.StopReason);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            summaryMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content,
                elapsedSeconds: CaptureElapsed(elapsedSw, summaryMessage));
        }
        finally
        {
            if (elapsedSw.IsRunning) CaptureElapsed(elapsedSw, summaryMessage);
            summaryMessage.IsStreaming = false;
        }
    }

    #endregion

    #region Request Building

    /// <summary>
    /// Builds an <see cref="AiRequest"/> from the current messages, selected preset settings,
    /// and optional workspace/RAG context.
    /// </summary>
    private async Task<AiRequest> CreateRequestAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<IAssistTool>? activeTools = null,
        string? systemPrompt = null)
    {
        activeTools ??= GetActiveTools();

        var workspaceText = WorkspaceContext is not null
            ? await WorkspaceContext.GetContextAsync()
            : null;

        // Append workspace folder paths so the AI knows absolute paths
        var folders = WorkspaceFolders;
        if (folders is { Count: > 0 })
        {
            var folderSection = "\n\n## Workspace\nCurrent workspace directories:\n"
                + string.Join("\n", folders.Select(f => $"- {f}"))
                + "\n\nWhen using `run_command` without an explicit working_directory, default to the first workspace directory listed above.";
            workspaceText = (workspaceText ?? "") + folderSection;
        }

        // Knowledge Base hint — if search_documents tool is available and a KB is selected
        if (activeTools.Any(t => t.Name == "search_documents") && !string.IsNullOrEmpty(KnowledgeBaseId))
        {
            var kbId = KnowledgeBaseId;
            workspaceText = (workspaceText ?? "")
                + $"\n\n## Knowledge Base\nUse `search_documents` to find relevant information before answering."
                + $"\nAlways pass kb_id=\"{kbId}\" when calling search_documents or get_document_chunk."
                + "\nIf initial search returns no results, retry with a lower threshold (e.g., 0.1) or different query terms.";
        }

        // Sub-Agent hint — when delegate_task tool is available
        if (activeTools.Any(t => t.Name == "delegate_task"))
        {
            workspaceText = (workspaceText ?? "")
                + "\n\n## Sub-Agent\n\n"
                + "You have a delegate_task tool that runs tasks in a separate context.\n\n"
                + "**ONLY delegate when ALL of these are true:**\n"
                + "- The task requires 5+ tool calls with intermediate reasoning\n"
                + "- The task is independent and does NOT need user clarification\n"
                + "- Running it in the main conversation would consume excessive context\n\n"
                + "**Do NOT delegate:**\n"
                + "- Simple lookups or searches (just call the tools directly)\n"
                + "- Tasks with fewer than 5 tool calls\n"
                + "- When you can answer by combining 2-3 tool results yourself\n\n"
                + "When delegating, specify allowed_tools to limit the sub-agent's tools.\n"
                + "You do NOT need to specify mcp_servers \u2014 inherited by default.\n"
                + "Multiple delegate_task calls in one response run as independent sub-tasks.";
        }

        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content;
        var chunks = ContextProvider is not null && lastUserMsg is not null
            ? await ContextProvider.RetrieveAsync(lastUserMsg)
            : null;

        var preset = SelectedModel;
        return new AiRequest
        {
            Messages = OrphanToolCancelInjector.Inject(ApplySummaryTruncation(messages)),
            SystemPrompt = systemPrompt ?? SystemPrompt,
            MemoryText = MemoryText,
            WorkspaceText = workspaceText,
            ContextChunks = chunks is { Count: > 0 } ? chunks : null,
            Temperature = preset?.Temperature ?? 0.7,
            MaxTokens = preset?.MaxTokens ?? 4096,
            Tools = activeTools is { Count: > 0 } ? activeTools : null,
            ThinkingEnabled = preset?.ThinkingEnabled ?? false,
            ThinkingBudget = preset?.ThinkingBudget
        };
    }

    /// <summary>
    /// Returns a view of <paramref name="messages"/> trimmed to start from the most recent
    /// summary node (inclusive). If no summary exists, returns the full list unchanged.
    /// The summary node's <see cref="ChatMessage.Content"/> is wrapped with a
    /// "[Previous conversation summary]" prefix at build time only — the stored content is unchanged.
    /// </summary>
    private static IReadOnlyList<ChatMessage> ApplySummaryTruncation(IReadOnlyList<ChatMessage> messages)
    {
        // Walk backwards to find the most recent summary node
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (messages[i].Summary is not null)
            {
                // Build truncated list: summary node (with wrapped content) + everything after
                var truncated = new List<ChatMessage>(messages.Count - i);
                var summary = messages[i];
                truncated.Add(new ChatMessage(summary.Id, ChatRole.Assistant,
                    $"[Previous conversation summary]\n{summary.Content}")
                {
                    ProviderName = summary.ProviderName,
                    ProviderModelId = summary.ProviderModelId,
                    ParentId = summary.ParentId,
                    Summary = summary.Summary
                });
                for (var j = i + 1; j < messages.Count; j++)
                    truncated.Add(messages[j]);
                return truncated;
            }
        }

        return messages;
    }

    #endregion

    #region Tool Approval & Elicitation

    /// <summary>
    /// Handles the Approved event from the ToolApprovalPanel.
    /// </summary>
    private void OnToolApproved(object? sender, string? userNote) => _approvalTcs?.TrySetResult((true, userNote));

    /// <summary>
    /// Handles the Rejected event from the ToolApprovalPanel.
    /// </summary>
    private void OnToolRejected(object? sender, string? userNote) => _approvalTcs?.TrySetResult((false, userNote));

    /// <summary>Handles the Submitted event from the ToolElicitationPanel.</summary>
    private void OnElicitationSubmitted(object? sender, IDictionary<string, object?> content) =>
        _elicitationTcs?.TrySetResult(("accept", content));

    /// <summary>Handles the Declined (Skip) event from the ToolElicitationPanel.</summary>
    private void OnElicitationDeclined(object? sender, EventArgs e) =>
        _elicitationTcs?.TrySetResult(("decline", null));

    /// <summary>Handles the Cancelled (ESC) event from the ToolElicitationPanel.</summary>
    private void OnElicitationCancelled(object? sender, EventArgs e) =>
        _elicitationTcs?.TrySetResult(("cancel", null));

    /// <summary>
    /// Shows the elicitation panel and awaits user input.
    /// Called by the MCP elicitation handler callback.
    /// </summary>
    /// <param name="toolName">The tool or operation name for the header.</param>
    /// <param name="serverName">The MCP server name for the badge.</param>
    /// <param name="message">Descriptive message shown below the header.</param>
    /// <param name="fields">The fields to render.</param>
    /// <returns>A tuple of (action, content) where action is "accept", "decline", or "cancel".</returns>
    public async Task<(string Action, IDictionary<string, object?>? Content)> RequestElicitationAsync(
        string toolName,
        string serverName,
        string message,
        IReadOnlyList<ElicitationFieldInfo> fields)
    {
        if (_elicitationPanel is null || _inputArea is null)
            return ("cancel", null);

        DiagnosticLogger.LogInfo($"[Elicitation] Requested: {toolName} (server={serverName})");

        _elicitationPanel.ToolName = GetToolDisplayName(toolName);
        _elicitationPanel.ServerName = serverName;
        _elicitationPanel.Message = message;
        _elicitationPanel.Fields = fields;
        _inputArea.Visibility = Visibility.Collapsed;
        _elicitationPanel.Visibility = Visibility.Visible;
        _elicitationPanel.Focus(FocusState.Programmatic);

        _elicitationTcs = new TaskCompletionSource<(string, IDictionary<string, object?>?)>();
        var (action, content) = await _elicitationTcs.Task;
        DiagnosticLogger.LogInfo($"[Elicitation] Result: {toolName} → {action}");

        _elicitationPanel.Visibility = Visibility.Collapsed;
        _inputArea.Visibility = Visibility.Visible;

        return (action, content);
    }

    #endregion
}
