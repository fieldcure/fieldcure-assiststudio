using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Helpers;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ChatPanel
{
    #region Tree Methods

    /// <summary>
    /// Registers a message in the conversation tree.
    /// When <paramref name="updateActiveChild"/> is <c>true</c>, the parent message's
    /// <see cref="ChatMessage.ActiveChildId"/> is set to this message so that
    /// save/restore preserves the user's branch selection.
    /// </summary>
    private void RegisterInTree(ChatMessage msg, bool updateActiveChild = true)
    {
        var key = msg.ParentId ?? TreeRootKey;
        if (!_childrenMap.TryGetValue(key, out var siblings))
        {
            siblings = [];
            _childrenMap[key] = siblings;
        }
        if (!siblings.Any(s => s.Id == msg.Id))
        {
            msg.SiblingIndex = siblings.Count;
            siblings.Add(msg);
        }

        // Tool-internal messages (assistant tool-call requests and tool results) are
        // not real branch points — they are part of the same response chain.
        // Exclude them from the visible sibling count so the UI does not show
        // spurious branch-navigation arrows when a tool chain lives alongside the
        // next user message under the same parent.
        static bool IsToolInternal(ChatMessage m) =>
            m.Role == ChatRole.Tool ||
            (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 });

        var visibleCount = siblings.Count(s => !IsToolInternal(s));
        foreach (var s in siblings)
            s.SiblingCount = IsToolInternal(s) ? 1 : Math.Max(visibleCount, 1);

        // Update the parent's active child pointer when on the active path
        if (updateActiveChild && msg.ParentId is not null)
        {
            var parent = _messages.FirstOrDefault(m => m.Id == msg.ParentId);
            if (parent is not null) parent.ActiveChildId = msg.Id;
        }
    }

    /// <summary>
    /// Finds the ID of the last message before <paramref name="idx"/> in <see cref="_messages"/>
    /// that has its own DOM element. Tool-internal messages (tool results, assistant tool-call
    /// requests) are rendered inline within the root assistant bubble and have no independent
    /// DOM node. This walks backwards to find a User or root Assistant message whose
    /// <c>msg-{id}</c> element actually exists in the WebView2 DOM.
    /// Returns <c>null</c> if no rendered message precedes the given index (clear entire chat).
    /// </summary>
    private string? FindRenderedMessageBefore(int idx)
    {
        for (var i = idx - 1; i >= 0; i--)
        {
            var m = _messages[i];
            // User messages always have their own DOM element
            if (m.Role == ChatRole.User) return m.Id;
            // Root assistant messages (no ToolCalls, or the initial streaming message) have DOM elements.
            // Tool-internal assistant messages (with ToolCalls) and Tool result messages
            // are rendered inline within the root assistant bubble above them.
            if (m.Role == ChatRole.Assistant && m.ToolCalls is not { Count: > 0 })
                return m.Id;
        }
        return null;
    }

    /// <summary>
    /// Builds a path from the given message to its deepest leaf,
    /// always following the last child (most recent branch) at each level.
    /// </summary>
    private List<ChatMessage> BuildPathToLeaf(ChatMessage start)
    {
        var path = new List<ChatMessage> { start };
        var current = start;
        while (_childrenMap.TryGetValue(current.Id, out var children) && children.Count > 0)
        {
            current = children[^1]; // follow last (most recent) child
            path.Add(current);
        }
        return path;
    }

    #endregion

    #region Message CRUD

    /// <summary>
    /// Returns a read-only snapshot of all messages in the conversation.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetMessages() => _messages;

    /// <summary>
    /// Returns all messages in the conversation tree (active path + all branches).
    /// Used for saving the full tree to disk.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetAllMessages()
    {
        if (_childrenMap.Count == 0) return _messages;
        return [.. _childrenMap.Values.SelectMany(v => v).Distinct()];
    }

    /// <summary>
    /// Registers a message in the tree without adding to the active path.
    /// Used for loading inactive branch messages from saved conversations.
    /// </summary>
    public void RegisterBranchMessage(ChatMessage msg)
    {
        RegisterInTree(msg, updateActiveChild: false);
    }

    /// <summary>
    /// Removes a message from both <see cref="_messages"/> and the tree
    /// (<see cref="_childrenMap"/> + parent <see cref="ChatMessage.ActiveChildId"/>).
    /// Used for ephemeral internal messages (e.g., the hidden "Continue writing..."
    /// user message) that must not survive into save/load.
    /// </summary>
    private void UnregisterFromTree(ChatMessage msg)
    {
        _messages.Remove(msg);

        var key = msg.ParentId ?? TreeRootKey;
        if (_childrenMap.TryGetValue(key, out var siblings))
        {
            siblings.RemoveAll(s => s.Id == msg.Id);
            if (siblings.Count == 0)
            {
                _childrenMap.Remove(key);
            }
            else
            {
                static bool IsToolInternal(ChatMessage m) =>
                    m.Role == ChatRole.Tool ||
                    (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 });
                var visibleCount = siblings.Count(s => !IsToolInternal(s));
                for (var i = 0; i < siblings.Count; i++)
                {
                    siblings[i].SiblingIndex = i;
                    siblings[i].SiblingCount = IsToolInternal(siblings[i]) ? 1 : Math.Max(visibleCount, 1);
                }
            }
        }

        if (msg.ParentId is not null)
        {
            var parent = _messages.FirstOrDefault(m => m.Id == msg.ParentId);
            if (parent is not null && parent.ActiveChildId == msg.Id)
            {
                parent.ActiveChildId = _childrenMap.TryGetValue(msg.ParentId, out var remaining) && remaining.Count > 0
                    ? remaining[^1].Id
                    : null;
            }
        }
    }

    /// <summary>
    /// Adds a previously saved message to the conversation (for restoring saved conversations).
    /// Messages added before the WebView is initialized will be rendered once initialization completes.
    /// </summary>
    public void AddRestoredMessage(ChatRole role, string content,
        string? providerName = null, string? providerModelId = null,
        string? id = null, string? parentId = null,
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null,
        string? activeChildId = null,
        IReadOnlyList<ChatAttachment>? attachments = null,
        IReadOnlyList<MediaContent>? toolMedia = null,
        string? thinkingContent = null,
        DateTime? timestamp = null,
        double? elapsedSeconds = null,
        int? tokenCount = null,
        SummaryMeta? summary = null)
    {
        var msg = id is not null
            ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Attachments = attachments ?? [], ToolMedia = toolMedia, ThinkingContent = thinkingContent, Timestamp = timestamp ?? DateTime.UtcNow, ElapsedSeconds = elapsedSeconds, TokenCount = tokenCount, Summary = summary }
            : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Attachments = attachments ?? [], ToolMedia = toolMedia, ThinkingContent = thinkingContent, Timestamp = timestamp ?? DateTime.UtcNow, ElapsedSeconds = elapsedSeconds, TokenCount = tokenCount, Summary = summary };
        RegisterInTree(msg);
        _messages.Add(msg);
    }

    /// <summary>
    /// Returns a snapshot of the current conversation messages for external consumers (e.g., SDK converters).
    /// </summary>
    public IReadOnlyList<ChatMessage> GetConversationSnapshot() => [.. _messages];

    /// <summary>
    /// Clears all messages and resets the chat panel to its empty state.
    /// </summary>
    public async void ClearConversation()
    {
        _streamingCts?.Cancel();
        _messages.Clear();
        if (_isInitialized && _chatWebView is not null)
        {
            await _renderer.SetThemeAsync(IsDarkTheme());
            await _renderer.InitializeAsync(_chatWebView);
            await ApplyThemeAsync();
        }

        // Switch back to empty state
        if (_chatLayout?.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            _chatLayout.Children.Remove(_inputArea);
            if (_inputArea is not null)
                _inputArea.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            if (_inputArea is not null)
                _emptyStateContent?.Children.Add(_inputArea);
            _chatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            if (_emptyStatePanel is not null)
                _emptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    #endregion

    #region User Message Core

    /// <summary>
    /// Core logic for adding a user message to the conversation tree and rendering it.
    /// </summary>
    private async Task<ChatMessage> AddUserMessageCoreAsync(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        var parentId = _messages.Count > 0 ? _messages[^1].Id : null;
        var userMessage = new ChatMessage(ChatRole.User, text) { Attachments = attachments, ParentId = parentId };
        RegisterInTree(userMessage);
        _messages.Add(userMessage);
        await _renderer.AppendUserMessageAsync(
            userMessage.Id, userMessage.Content, userMessage.Timestamp.ToString("O"),
            userMessage.Attachments, userMessage.SiblingIndex, userMessage.SiblingCount);
        MessageAdded?.Invoke(this, userMessage);
        DiagnosticLogger.LogInfo($"[Chat] User message sent, attachments={attachments.Count}");
        return userMessage;
    }

    #endregion

    #region Restored Message Rendering

    /// <summary>
    /// Renders all pre-existing messages in <see cref="_messages"/> to the WebView2.
    /// Called from <see cref="OnLoaded"/> and from the App layer after pending messages are flushed
    /// when messages arrive after initialization.
    /// </summary>
    public async Task RenderRestoredMessagesAsync()
    {
        if (!_isInitialized || _messages.Count == 0 || _hasRenderedRestored) return;
        _hasRenderedRestored = true;

        DiagnosticLogger.LogInfo($"[Chat] RenderRestoredMessages: {_messages.Count} messages");
        SwitchToChatLayout();

        var renderedCount = 0;
        var pendingChain = new List<ChatMessage>();

        foreach (var msg in _messages)
        {
            if (msg.Role == ChatRole.User)
            {
                // Finalize any pending assistant chain before rendering next user message
                if (pendingChain.Count > 0)
                {
                    await RenderAssistantBubbleAsync(pendingChain);
                    pendingChain.Clear();
                    renderedCount++;
                }

                await _renderer.AppendUserMessageAsync(
                    msg.Id, msg.Content ?? "", msg.Timestamp.ToString("O"), msg.Attachments,
                    msg.SiblingIndex, msg.SiblingCount);
                renderedCount++;
            }
            else if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                // Intermediate tool-call message — collect into pending chain
                pendingChain.Add(msg);
            }
            else if (msg.Role == ChatRole.Tool)
            {
                // Tool result — collect into pending chain (consumed by ToolCallId matching)
                pendingChain.Add(msg);
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                // New root assistant message — finalize previous chain if any
                if (pendingChain.Count > 0)
                {
                    await RenderAssistantBubbleAsync(pendingChain);
                    pendingChain.Clear();
                    renderedCount++;
                }

                pendingChain.Add(msg);
            }
        }

        // Finalize last pending assistant chain
        if (pendingChain.Count > 0)
        {
            await RenderAssistantBubbleAsync(pendingChain);
            renderedCount++;
        }

        DiagnosticLogger.LogInfo($"[Chat] RenderRestoredMessages: {renderedCount} bubbles from {_messages.Count} messages");
    }

    /// <summary>
    /// Renders a chain of assistant messages (root + intermediate tool-calling rounds)
    /// into a single bubble with interleaved text segments and tool blocks.
    /// The <paramref name="chain"/> must contain the root assistant message first,
    /// followed by alternating tool-call assistant / tool-result messages.
    /// </summary>
    private async Task RenderAssistantBubbleAsync(IReadOnlyList<ChatMessage> chain)
    {
        if (chain.Count == 0) return;

        var root = chain[0];
        var isSummary = root.Summary is not null;
        await _renderer.BeginAssistantMessageAsync(root.Id, root.ProviderName, root.ProviderModelId, isSummary);

        // Restore thinking block
        if (!string.IsNullOrEmpty(root.ThinkingContent))
        {
            await _renderer.BeginThinkingBlockAsync(root.Id);
            await _renderer.AppendThinkingTokenAsync(root.Id, root.ThinkingContent);
            await _renderer.EndThinkingBlockAsync(root.Id);
        }

        var consumedLength = 0;

        // Process intermediate tool-calling rounds
        for (var i = 1; i < chain.Count; i++)
        {
            var msg = chain[i];

            if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                // Delta text segment
                if (!string.IsNullOrEmpty(msg.Content))
                {
                    await _renderer.AppendRenderedSegmentAsync(root.Id, msg.Content);
                    consumedLength += msg.Content.Length;
                }

                // Tool blocks with results
                foreach (var tc in msg.ToolCalls)
                {
                    var resultMsg = chain.FirstOrDefault(r =>
                        r.Role == ChatRole.Tool && r.ToolCallId == tc.Id);

                    if (tc.FunctionName == "search_documents" && resultMsg?.Content is not null)
                    {
                        await _renderer.AppendSearchResultBlockAsync(
                            root.Id, resultMsg.Content, tc.FunctionName);
                    }
                    else
                    {
                        await _renderer.AppendToolBlockAsync(
                            root.Id, tc.FunctionName, tc.Arguments,
                            resultMsg?.Content, null,
                            resultMsg?.Content?.Contains("\"error\"") == true);
                    }

                    if (resultMsg?.ToolMedia is { Count: > 0 } mediaItems)
                    {
                        foreach (var media in mediaItems)
                            await _renderer.AppendToolMediaAsync(root.Id, media);
                    }
                }
            }
            // Tool messages are consumed above via ToolCallId matching — skip.
        }

        // Finalize with remaining text, passing saved timestamp, elapsed time, and token count
        var finalSegment = (root.Content?.Length > consumedLength)
            ? root.Content[consumedLength..]
            : "";
        await _renderer.FinalizeMessageAsync(root.Id, finalSegment,
            tokenCount: root.TokenCount ?? 0,
            timestamp: root.Timestamp.ToString("O"),
            elapsedSeconds: root.ElapsedSeconds,
            coveredTokenCount: root.Summary?.CoveredTokenCount ?? 0);
    }

    #endregion

    #region Branch Operations

    /// <summary>
    /// Handles the retry request from the renderer to re-send a user message and get a new response.
    /// </summary>
    private async void OnRetryRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;

        var userMessage = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Id == messageId);
        if (userMessage is null) return;

        // Find the index of this user message and remove everything after it
        var idx = _messages.IndexOf(userMessage);
        if (idx < 0) return;
        while (_messages.Count > idx + 1)
        {
            _messages.RemoveAt(_messages.Count - 1);
        }
        await _renderer.RemoveMessagesAfterAsync(messageId);

        // Re-send with the same text
        await StreamAssistantResponseAsync(userMessage);
    }

    /// <summary>
    /// Handles the edit request from the renderer to update a user message and re-stream a response.
    /// </summary>
    private async void OnEditRequested(object? sender, (string MessageId, string NewText) e)
    {
        if (!_isInitialized) return;

        var original = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Id == e.MessageId);
        if (original is null) return;

        // Create sibling message (same ParentId as original → branching)
        var edited = new ChatMessage(ChatRole.User, e.NewText)
        {
            ParentId = original.ParentId,
            Attachments = original.Attachments,
        };
        RegisterInTree(edited);

        // Explicitly point the parent's active child to the new branch.
        // RegisterInTree does this via _messages lookup, but the parent may be
        // a tool-internal message deep in a tool loop chain. By searching the tree
        // directly we ensure the pointer is set regardless of _messages state.
        if (edited.ParentId is not null)
        {
            var parentInTree = _childrenMap.Values
                .SelectMany(v => v)
                .FirstOrDefault(m => m.Id == edited.ParentId);
            if (parentInTree is not null)
                parentInTree.ActiveChildId = edited.Id;
        }

        // Switch active path: remove from original's position onward
        var idx = _messages.IndexOf(original);
        if (idx < 0) return;
        while (_messages.Count > idx)
            _messages.RemoveAt(_messages.Count - 1);
        _messages.Add(edited);

        // Update renderer: remove old messages, render new branch with navigator.
        // Tool loop messages (Assistant+ToolCalls, Tool results) are rendered inline
        // within a single Assistant bubble — they don't have their own DOM element.
        // Walk backwards to find the root Assistant bubble that actually exists in DOM.
        var removeAfterId = FindRenderedMessageBefore(idx);
        if (removeAfterId is not null)
            await _renderer.RemoveMessagesAfterAsync(removeAfterId);
        else
            await _renderer.ClearMessagesAsync();

        await _renderer.AppendUserMessageAsync(
            edited.Id, edited.Content, edited.Timestamp.ToString("O"),
            edited.Attachments, edited.SiblingIndex, edited.SiblingCount);

        // Notify the host so provider-less integrations (e.g. Anthropic SDK sample driving
        // the turn via BeginAnthropicTurn) can stream the response themselves.
        UserMessageSubmitted?.Invoke(this, new MessageSentEventArgs(edited.Content, edited.Attachments));

        // When external code drives the conversation, skip the internal provider flow.
        if (DisableInternalSendFlow) return;
        if (Provider is null) return;

        // Stream new response
        await StreamAssistantResponseAsync(edited);
    }

    /// <summary>
    /// Handles branch switch requests from the chat UI.
    /// Rebuilds the active path from root to the selected branch's leaf.
    /// </summary>
    private async void OnBranchSwitchRequested(object? sender, (string MessageId, int Direction) e)
    {
        var current = _messages.FirstOrDefault(m => m.Id == e.MessageId);
        if (current is null) return;

        // Find sibling in the tree
        if (!_childrenMap.TryGetValue(current.ParentId ?? TreeRootKey, out var siblings)) return;
        var newIndex = current.SiblingIndex + e.Direction;
        if (newIndex < 0 || newIndex >= siblings.Count) return;
        var target = siblings[newIndex];

        // Update parent's ActiveChildId to track the user's branch selection
        var parent = _messages.FirstOrDefault(m => m.Id == current.ParentId);
        if (parent is not null) parent.ActiveChildId = target.Id;
        BranchChanged?.Invoke(this, EventArgs.Empty);

        // Truncate active path from current message's position
        var idx = _messages.IndexOf(current);
        if (idx < 0) return;
        while (_messages.Count > idx)
            _messages.RemoveAt(_messages.Count - 1);

        // Walk from target down to its deepest leaf (following last child at each level)
        var path = BuildPathToLeaf(target);
        foreach (var msg in path)
            _messages.Add(msg);

        // Re-render from the branch point.
        // Tool loop messages don't have their own DOM element — walk back to the
        // root Assistant bubble that is actually rendered.
        var removeAfterId = FindRenderedMessageBefore(idx);
        if (removeAfterId is not null)
            await _renderer.RemoveMessagesAfterAsync(removeAfterId);
        else
            await _renderer.ClearMessagesAsync();

        var pendingChain = new List<ChatMessage>();

        foreach (var msg in path)
        {
            if (msg.Role == ChatRole.User)
            {
                if (pendingChain.Count > 0)
                {
                    await RenderAssistantBubbleAsync(pendingChain);
                    pendingChain.Clear();
                }

                await _renderer.AppendUserMessageAsync(
                    msg.Id, msg.Content, msg.Timestamp.ToString("O"),
                    msg.Attachments, msg.SiblingIndex, msg.SiblingCount);
            }
            else if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                pendingChain.Add(msg);
            }
            else if (msg.Role == ChatRole.Tool)
            {
                pendingChain.Add(msg);
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                if (pendingChain.Count > 0)
                {
                    await RenderAssistantBubbleAsync(pendingChain);
                    pendingChain.Clear();
                }

                pendingChain.Add(msg);
            }
        }

        if (pendingChain.Count > 0)
            await RenderAssistantBubbleAsync(pendingChain);
    }

    #endregion
}
