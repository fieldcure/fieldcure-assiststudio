using System.Collections;
using System.Collections.ObjectModel;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using FieldCure.AssistStudio.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Theme mode for the ChatPanel.
/// </summary>
public enum ChatTheme
{
    /// <summary>Follow the system/app theme (default).</summary>
    System,
    /// <summary>Force light theme.</summary>
    Light,
    /// <summary>Force dark theme.</summary>
    Dark
}

/// <summary>
/// A templated control that provides a complete chat experience with message streaming,
/// attachments, preset selection, and conversation management. Default style is defined in Generic.xaml.
/// </summary>
public sealed class ChatPanel : Control
{
    /// <summary>Identifies the <see cref="Provider"/> dependency property.</summary>
    public static readonly DependencyProperty ProviderProperty =
        DependencyProperty.Register(nameof(Provider), typeof(IAiProvider), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Placeholder"/> dependency property.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(ChatPanel),
            new PropertyMetadata("Type a message..."));

    /// <summary>Identifies the <see cref="SystemPrompt"/> dependency property.</summary>
    public static readonly DependencyProperty SystemPromptProperty =
        DependencyProperty.Register(nameof(SystemPrompt), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Theme"/> dependency property.</summary>
    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(ChatTheme), typeof(ChatPanel),
            new PropertyMetadata(ChatTheme.System, OnThemePropertyChanged));

    /// <summary>Identifies the <see cref="AvailablePresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    /// <summary>Identifies the <see cref="AvailablePromptPresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePromptPresetsProperty =
        DependencyProperty.Register(nameof(AvailablePromptPresets), typeof(IList<PromptPreset>), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailablePromptPresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPromptPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPromptPresetProperty =
        DependencyProperty.Register(nameof(SelectedPromptPreset), typeof(PromptPreset), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedPromptPresetChanged));

    /// <summary>Identifies the <see cref="IsDebugMode"/> dependency property.</summary>
    public static readonly DependencyProperty IsDebugModeProperty =
        DependencyProperty.Register(nameof(IsDebugMode), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false, OnIsDebugModeChanged));

    /// <summary>Identifies the <see cref="Title"/> dependency property.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null, OnTitlePropertyChanged));

    private static void OnThemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized)
        {
            _ = panel.ApplyThemeAsync();
        }
    }

    private static void OnIsDebugModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized)
        {
            _ = panel._renderer.SetDebugModeAsync((bool)e.NewValue);
        }
    }

    private static void OnTitlePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
        {
            var title = e.NewValue as string;
            var hasTitle = !string.IsNullOrEmpty(title);
            if (panel._titleText is not null)
                panel._titleText.Text = title ?? "";
            if (panel._titleBar is not null)
                panel._titleBar.Visibility = hasTitle
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;

            if (hasTitle)
                panel.UpdateRefreshTooltip();
        }
    }

    private readonly WebViewChatRenderer _renderer = new();
    private readonly ObservableCollection<ChatMessage> _messages = [];
    private CancellationTokenSource? _streamingCts;
    private bool _isInitialized;
    private bool _titleGenerated;

    // Template parts
    private Grid? _rootGrid;
    private Grid? _emptyStatePanel;
    private StackPanel? _emptyStateContent;
    private InputContainer? _inputArea;
    private Grid? _chatLayout;
    private StackPanel? _titleBar;
    private TextBlock? _titleText;
    private Button? _titleEditButton;
    private Button? _titleRefreshButton;
    private WebView2? _chatWebView;

    /// <summary>
    /// Maximum input tokens before auto-summarization triggers. 0 = disabled (default).
    /// </summary>
    public int MaxInputTokens { get; set; } = 0;

    /// <summary>
    /// Number of recent conversation turns to keep when summarizing.
    /// </summary>
    public int RecentTurnsToKeep { get; set; } = 10;

    /// <summary>
    /// Enable automatic conversation summarization when input tokens exceed MaxInputTokens.
    /// </summary>
    public bool AutoSummarize { get; set; } = false;

    /// <summary>
    /// Provider used for utility tasks (title generation, summarization).
    /// Falls back to the main Provider if not set.
    /// </summary>
    public IAiProvider? UtilityProvider { get; set; }

    /// <summary>
    /// Enable automatic title generation after the first assistant response.
    /// </summary>
    public bool AutoTitle { get; set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatPanel"/> class.
    /// </summary>
    public ChatPanel()
    {
        DefaultStyleKey = typeof(ChatPanel);
        _renderer.ContinueRequested += OnContinueRequested;
        _renderer.MessageCopyRequested += OnMessageCopyRequested;
        _renderer.RetryRequested += OnRetryRequested;
        _renderer.EditRequested += OnEditRequested;
        _renderer.SummarizeRequested += OnSummarizeRequested;
    }

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old event handlers
        if (_inputArea is not null)
        {
            _inputArea.MessageSent -= OnMessageSent;
            _inputArea.PresetChanged -= OnInputPresetChanged;
            _inputArea.PromptPresetChanged -= OnInputPromptPresetChanged;
        }
        if (_rootGrid is not null)
        {
            _rootGrid.DragOver -= OnDragOver;
            _rootGrid.Drop -= OnDrop;
        }
        if (_titleEditButton is not null)
            _titleEditButton.Click -= OnTitleEditClick;
        if (_titleRefreshButton is not null)
            _titleRefreshButton.Click -= OnTitleRefreshClick;

        // Get template parts
        _rootGrid = GetTemplateChild("PART_RootGrid") as Grid;
        _emptyStatePanel = GetTemplateChild("PART_EmptyStatePanel") as Grid;
        _emptyStateContent = GetTemplateChild("PART_EmptyStateContent") as StackPanel;
        _inputArea = GetTemplateChild("PART_InputArea") as InputContainer;
        _chatLayout = GetTemplateChild("PART_ChatLayout") as Grid;
        _titleBar = GetTemplateChild("PART_TitleBar") as StackPanel;
        _titleText = GetTemplateChild("PART_TitleText") as TextBlock;
        _titleEditButton = GetTemplateChild("PART_TitleEditButton") as Button;
        _titleRefreshButton = GetTemplateChild("PART_TitleRefreshButton") as Button;
        _chatWebView = GetTemplateChild("PART_ChatWebView") as WebView2;

        // Set initial background to match CSS --bg-primary (before WebView2 loads)
        if (_rootGrid is not null)
            _rootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(LightBg);

        // Attach event handlers and sync current property values
        if (_inputArea is not null)
        {
            _inputArea.MessageSent += OnMessageSent;
            _inputArea.PresetChanged += OnInputPresetChanged;
            _inputArea.PromptPresetChanged += OnInputPromptPresetChanged;

            // Push current values (may have been set before template was applied)
            if (AvailablePresets is { } presets)
                _inputArea.AvailablePresets = presets;
            if (SelectedPreset is { } selectedPreset)
                _inputArea.SelectedPreset = selectedPreset;
            if (AvailablePromptPresets is { } promptPresets)
                _inputArea.AvailablePromptPresets = promptPresets;
            if (SelectedPromptPreset is { } selectedPromptPreset)
            {
                _inputArea.SelectedPromptPreset = selectedPromptPreset;
                _inputArea.SelectPromptPresetInCombo(selectedPromptPreset);
            }
        }
        if (_rootGrid is not null)
        {
            _rootGrid.DragOver += OnDragOver;
            _rootGrid.Drop += OnDrop;
        }
        if (_titleEditButton is not null)
            _titleEditButton.Click += OnTitleEditClick;
        if (_titleRefreshButton is not null)
            _titleRefreshButton.Click += OnTitleRefreshClick;

        // Subscribe to Loaded for WebView2 initialization
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Gets or sets the AI provider used for streaming chat responses.
    /// </summary>
    public IAiProvider? Provider
    {
        get => (IAiProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown in the input area.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets the system prompt included with every AI request.
    /// </summary>
    public string? SystemPrompt
    {
        get => (string?)GetValue(SystemPromptProperty);
        set => SetValue(SystemPromptProperty, value);
    }

    /// <summary>
    /// Gets or sets the theme mode for the chat panel (System, Light, or Dark).
    /// </summary>
    public ChatTheme Theme
    {
        get => (ChatTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available provider presets shown in the input area dropdown.
    /// </summary>
    public IList? AvailablePresets
    {
        get => (IList?)GetValue(AvailablePresetsProperty);
        set => SetValue(AvailablePresetsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected provider preset.
    /// </summary>
    public ProviderPreset? SelectedPreset
    {
        get => (ProviderPreset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available prompt presets shown in the input area dropdown.
    /// </summary>
    public IList<PromptPreset>? AvailablePromptPresets
    {
        get => (IList<PromptPreset>?)GetValue(AvailablePromptPresetsProperty);
        set => SetValue(AvailablePromptPresetsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected prompt preset.
    /// </summary>
    public PromptPreset? SelectedPromptPreset
    {
        get => (PromptPreset?)GetValue(SelectedPromptPresetProperty);
        set => SetValue(SelectedPromptPresetProperty, value);
    }

    /// <summary>
    /// Enables debug mode: adds "Copy Request" / "Copy Response" buttons to the last
    /// message pair, allowing inspection of the actual API request body and raw response.
    /// </summary>
    public bool IsDebugMode
    {
        get => (bool)GetValue(IsDebugModeProperty);
        set => SetValue(IsDebugModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the conversation title displayed in the title bar.
    /// </summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Occurs when the user selects a different provider preset.
    /// </summary>
    public event EventHandler<ProviderPreset>? PresetChanged;

    /// <summary>
    /// Occurs when a new message (user or assistant) is added to the conversation.
    /// </summary>
    public event EventHandler<ChatMessage>? MessageAdded;

    /// <summary>
    /// Occurs when a conversation title is generated or regenerated by the AI provider.
    /// </summary>
    public event EventHandler<string>? TitleGenerated;

    /// <summary>
    /// Occurs when the user clicks the title edit button.
    /// </summary>
    public event EventHandler<string>? TitleEditRequested;

    /// <summary>
    /// Returns a read-only snapshot of all messages in the conversation.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetMessages() => _messages;

    /// <summary>
    /// Adds a previously saved message to the conversation (for restoring saved conversations).
    /// Messages added before the WebView is initialized will be rendered once initialization completes.
    /// </summary>
    public void AddRestoredMessage(ChatRole role, string content,
        string? providerName = null, string? providerModelId = null)
    {
        var msg = new ChatMessage(role, content)
        {
            ProviderName = providerName,
            ProviderModelId = providerModelId,
        };
        _messages.Add(msg);
    }

    /// <summary>
    /// Manually trigger conversation summarization.
    /// Compresses older messages into a system summary, keeping the most recent turns.
    /// </summary>
    public async Task SummarizeConversationAsync()
    {
        if (Provider is null || _messages.Count <= RecentTurnsToKeep) return;
        await SummarizeHistoryAsync(CancellationToken.None);
    }

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

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            if (_chatWebView is null) return;

            await _renderer.InitializeAsync(_chatWebView);
            _isInitialized = true;
            await ApplyThemeAsync();
            await ApplyLocaleStringsAsync();
            if (IsDebugMode)
                await _renderer.SetDebugModeAsync(true);

            // Render any pre-existing messages (restored conversations)
            if (_messages.Count > 0)
            {
                SwitchToChatLayout();
            }
            foreach (var msg in _messages)
            {
                if (msg.Role == ChatRole.User)
                {
                    await _renderer.AppendUserMessageAsync(
                        msg.Id, msg.Content, msg.Timestamp.ToString("O"), msg.Attachments);
                }
                else if (msg.Role == ChatRole.Assistant)
                {
                    await _renderer.BeginAssistantMessageAsync(
                        msg.Id, msg.ProviderName, msg.ProviderModelId);
                    await _renderer.FinalizeMessageAsync(msg.Id, msg.Content);
                }
            }

            // Listen for theme changes
            ActualThemeChanged += async (_, _) => await ApplyThemeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WebView2 init failed: {ex.Message}");
        }
    }

    private void SwitchToChatLayout()
    {
        if (_chatLayout is null || _emptyStatePanel is null ||
            _emptyStateContent is null || _inputArea is null) return;
        if (_chatLayout.Visibility == Microsoft.UI.Xaml.Visibility.Visible) return;

        _emptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        _chatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        // Move InputArea from EmptyStatePanel into ChatLayout as Row 2
        _emptyStateContent.Children.Remove(_inputArea);
        _inputArea.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        Grid.SetRow(_inputArea, 2);
        _chatLayout.Children.Add(_inputArea);
    }

    private async void OnMessageSent(object? sender, MessageSentEventArgs e)
    {
        if (!_isInitialized) return;
        if (string.IsNullOrWhiteSpace(e.Text) && e.Attachments.Count == 0) return;

        SwitchToChatLayout();

        // Add user message with attachments
        var userMessage = new ChatMessage(ChatRole.User, e.Text) { Attachments = e.Attachments };
        _messages.Add(userMessage);
        await _renderer.AppendUserMessageAsync(
            userMessage.Id, userMessage.Content, userMessage.Timestamp.ToString("O"),
            userMessage.Attachments);
        MessageAdded?.Invoke(this, userMessage);

        // Stream assistant response
        if (Provider is null)
        {
            System.Diagnostics.Debug.WriteLine("ChatPanel: Provider is null when sending message");
            var errorMsg = new ChatMessage(ChatRole.Assistant) { Content = "[Error: No AI provider configured]" };
            _messages.Add(errorMsg);
            await _renderer.BeginAssistantMessageAsync(errorMsg.Id, "Error", null);
            await _renderer.FinalizeMessageAsync(errorMsg.Id, errorMsg.Content);
            return;
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId
        };
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id, Provider.ProviderName, Provider.ModelId);
        MessageAdded?.Invoke(this, assistantMessage);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = CreateRequest(_messages.ToList());

            await foreach (var token in Provider.StreamAsync(request, ct))
            {
                assistantMessage.Content += token;
                await _renderer.AppendTokenAsync(assistantMessage.Id, token);
            }

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated, Provider.LastUsage?.TotalTokens ?? 0);

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(userMessage.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);

            TryGenerateTitleAsync();

            // Auto-summarize if enabled and token threshold exceeded
            if (AutoSummarize && MaxInputTokens > 0 && Provider.LastUsage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await SummarizeHistoryAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine("ChatPanel: Streaming cancelled");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ChatPanel: Streaming error: {ex}");
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            if (_inputArea is not null)
                _inputArea.IsInputEnabled = true;
        }
    }

    private void OnMessageCopyRequested(object? sender, string messageId)
    {
        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null || string.IsNullOrEmpty(message.Content)) return;

        var dp = new DataPackage();
        dp.SetText(message.Content);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
    }

    private async void OnContinueRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;

        // Find the assistant message to continue
        var assistantMessage = _messages.LastOrDefault(m =>
            m.Role == ChatRole.Assistant && m.Id == messageId);
        if (assistantMessage is null) return;

        // Add a user message asking to continue (not shown in UI)
        var continueMessage = new ChatMessage(ChatRole.User, "Continue writing from where you left off.");
        _messages.Add(continueMessage);

        // Resume the existing assistant message bubble for continued streaming
        assistantMessage.IsStreaming = true;
        await _renderer.ResumeMessageAsync(assistantMessage.Id, assistantMessage.Content);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = CreateRequest(_messages.ToList());

            var continuationText = "";
            await foreach (var token in Provider.StreamAsync(request, ct))
            {
                continuationText += token;
                await _renderer.AppendTokenAsync(assistantMessage.Id, token);
            }

            // Append continuation to the original assistant message content
            assistantMessage.Content += continuationText;

            // Remove the continue user message from history -- replace with combined assistant content
            _messages.Remove(continueMessage);

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated, Provider.LastUsage?.TotalTokens ?? 0);

            if (IsDebugMode)
            {
                // Find the original user message for this assistant response
                var idx2 = _messages.IndexOf(assistantMessage);
                var origUserMsg = idx2 > 0 ? _messages[idx2 - 1] : null;
                if (origUserMsg is not null)
                    await _renderer.SetDebugDataAsync(origUserMsg.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);
            }
        }
        catch (OperationCanceledException)
        {
            // Streaming was cancelled
        }
        catch (Exception ex)
        {
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            if (_inputArea is not null)
                _inputArea.IsInputEnabled = true;
        }
    }

    private async Task SummarizeHistoryAsync(CancellationToken ct)
    {
        if (Provider is null) return;

        // Keep the most recent turns
        var turnsToKeep = Math.Min(RecentTurnsToKeep, _messages.Count);
        var oldMessages = _messages.Take(_messages.Count - turnsToKeep).ToList();
        if (oldMessages.Count == 0) return;

        // Build summarization prompt
        var historyText = string.Join("\n",
            oldMessages.Select(m => $"{m.Role}: {m.Content}"));

        var summaryRequest = new AiRequest
        {
            Messages =
            [
                new ChatMessage(ChatRole.User,
                    "Summarize the following conversation concisely, preserving key context and decisions:\n\n" +
                    historyText)
            ],
            SystemPrompt = "You are a helpful assistant that creates concise conversation summaries."
        };

        try
        {
            var summary = await Provider.CompleteAsync(summaryRequest, ct);

            // Replace old messages with a summary system message
            for (var i = 0; i < oldMessages.Count; i++)
            {
                _messages.RemoveAt(0);
            }

            _messages.Insert(0, new ChatMessage(ChatRole.System,
                $"[Previous conversation summary]\n{summary}"));
        }
        catch
        {
            // Summarization failed -- keep messages as-is
        }
    }

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

    private async void OnEditRequested(object? sender, (string MessageId, string NewText) e)
    {
        if (!_isInitialized || Provider is null) return;

        var userMessage = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Id == e.MessageId);
        if (userMessage is null) return;

        // Update message content
        userMessage.Content = e.NewText;

        // Remove everything after this user message
        var idx = _messages.IndexOf(userMessage);
        if (idx < 0) return;
        while (_messages.Count > idx + 1)
        {
            _messages.RemoveAt(_messages.Count - 1);
        }
        await _renderer.RemoveMessagesAfterAsync(e.MessageId);

        // Update the user bubble text in the UI
        if (_chatWebView is not null)
        {
            var escaped = System.Text.Json.JsonSerializer.Serialize(e.NewText);
            await _chatWebView.ExecuteScriptAsync(
                $"document.querySelector('#msg-{e.MessageId} .message-bubble').textContent = {escaped}");
        }

        // Re-send
        await StreamAssistantResponseAsync(userMessage);
    }

    private async void OnSummarizeRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;

        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null || string.IsNullOrEmpty(message.Content)) return;

        // Create a summarization request for this single message
        var summaryRequest = new AiRequest
        {
            Messages =
            [
                new ChatMessage(ChatRole.User,
                    "Summarize the following text concisely:\n\n" + message.Content)
            ],
            SystemPrompt = "You are a helpful assistant. Provide a concise summary."
        };

        var summaryMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId
        };
        _messages.Add(summaryMessage);
        await _renderer.BeginAssistantMessageAsync(summaryMessage.Id, Provider.ProviderName, Provider.ModelId);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            await foreach (var token in Provider.StreamAsync(summaryRequest, ct))
            {
                summaryMessage.Content += token;
                await _renderer.AppendTokenAsync(summaryMessage.Id, token);
            }
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content, Provider.IsTruncated);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            summaryMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content);
        }
        finally
        {
            summaryMessage.IsStreaming = false;
            if (_inputArea is not null)
                _inputArea.IsInputEnabled = true;
        }
    }

    private async Task StreamAssistantResponseAsync(ChatMessage userMessage)
    {
        if (Provider is null) return;

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId
        };
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id, Provider.ProviderName, Provider.ModelId);
        MessageAdded?.Invoke(this, assistantMessage);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = CreateRequest(_messages.ToList());

            await foreach (var token in Provider.StreamAsync(request, ct))
            {
                assistantMessage.Content += token;
                await _renderer.AppendTokenAsync(assistantMessage.Id, token);
            }

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated, Provider.LastUsage?.TotalTokens ?? 0);

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(userMessage.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);

            if (AutoSummarize && MaxInputTokens > 0 && Provider.LastUsage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await SummarizeHistoryAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            if (_inputArea is not null)
                _inputArea.IsInputEnabled = true;
        }
    }

    private void OnInputPresetChanged(object? sender, ProviderPreset preset)
    {
        SelectedPreset = preset;
        PresetChanged?.Invoke(this, preset);
    }

    private static void OnAvailablePresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.AvailablePresets = e.NewValue as IList;
        }
    }

    private static void OnSelectedPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is ProviderPreset preset)
        {
            if (panel._inputArea is not null)
                panel._inputArea.SelectedPreset = preset;
            panel.UpdatePlaceholderWithProvider(preset.Name);
        }
    }

    private void UpdatePlaceholderWithProvider(string providerName)
    {
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader("AssistStudio.Controls/Resources");
            var format = loader.GetString("InputContainer_AskProvider");
            if (!string.IsNullOrEmpty(format))
            {
                Placeholder = string.Format(format, providerName);
                return;
            }
        }
        catch { /* fallback */ }

        Placeholder = $"Ask {providerName}...";
    }

    private static void OnAvailablePromptPresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is IList<PromptPreset> presets)
        {
            if (panel._inputArea is not null)
                panel._inputArea.AvailablePromptPresets = presets;
        }
    }

    private static void OnSelectedPromptPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is PromptPreset preset)
        {
            if (panel._inputArea is not null)
            {
                panel._inputArea.SelectedPromptPreset = preset;
                panel._inputArea.SelectPromptPresetInCombo(preset);
            }
            // Update the actual system prompt used in requests
            panel.SystemPrompt = preset.Text;
        }
    }

    private void OnInputPromptPresetChanged(object? sender, PromptPreset preset)
    {
        SelectedPromptPreset = preset;
        SystemPrompt = preset.Text;
    }

    private void OnTitleEditClick(object sender, RoutedEventArgs e)
    {
        TitleEditRequested?.Invoke(this, Title ?? "");
    }

    private async void OnTitleRefreshClick(object sender, RoutedEventArgs e)
    {
        await RegenerateTitleAsync();
    }

    private void UpdateRefreshTooltip()
    {
        var provider = UtilityProvider ?? Provider;
        var providerName = provider?.ProviderName ?? "";

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader("AssistStudio.Controls/Resources");
            var format = loader.GetString("Chat_RegenerateTitle");
            if (!string.IsNullOrEmpty(format) && !string.IsNullOrEmpty(providerName) && _titleRefreshButton is not null)
            {
                ToolTipService.SetToolTip(_titleRefreshButton, string.Format(format, providerName));
                return;
            }
        }
        catch { /* fallback */ }

        if (_titleRefreshButton is not null)
        {
            ToolTipService.SetToolTip(_titleRefreshButton,
                string.IsNullOrEmpty(providerName)
                    ? "Regenerate title"
                    : $"Regenerate title with {providerName}");
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        if (_inputArea is not null)
            await _inputArea.AddFilesAsync(items);
    }

    // Background colors matching chat.html --bg-primary (opaque, no alpha issues)
    private static readonly Windows.UI.Color LightBg = Windows.UI.Color.FromArgb(255, 0xF5, 0xF5, 0xF5);
    private static readonly Windows.UI.Color DarkBg = Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);

    private async void TryGenerateTitleAsync()
    {
        if (_titleGenerated || !AutoTitle) return;
        _titleGenerated = true;
        await GenerateTitleCoreAsync();
    }

    /// <summary>
    /// Regenerates the conversation title from the full message history.
    /// </summary>
    public async Task RegenerateTitleAsync()
    {
        await GenerateTitleCoreAsync();
    }

    private async Task GenerateTitleCoreAsync()
    {
        var provider = UtilityProvider ?? Provider;
        if (provider is null or MockProvider) return;

        // Build context from conversation history
        var userMsg = _messages.FirstOrDefault(m => m.Role == ChatRole.User);
        var assistantMsg = _messages.FirstOrDefault(m => m.Role == ChatRole.Assistant);
        if (userMsg is null || assistantMsg is null) return;

        // Use more context for regeneration -- include recent messages
        var contextParts = new List<string>();
        foreach (var msg in _messages.Take(6))
        {
            var role = msg.Role == ChatRole.User ? "User" : "Assistant";
            var content = msg.Content.Length > 150 ? msg.Content[..150] : msg.Content;
            contextParts.Add($"{role}: {content}");
        }
        var context = string.Join("\n", contextParts);

        try
        {
            var titleRequest = new AiRequest
            {
                Messages =
                [
                    new ChatMessage(ChatRole.User,
                        $"{context}\n\nGenerate a short title (max 6 words) for this conversation. Reply with ONLY the title, no quotes or punctuation.")
                ],
                SystemPrompt = "You generate concise conversation titles.",
                Temperature = 0.5,
                MaxTokens = 200
            };

            var title = await provider.CompleteAsync(titleRequest);
            title = title.Trim().Trim('"', '\'', '.').Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = userMsg.Content.Length > 40
                    ? userMsg.Content[..40].TrimEnd() + "\u2026"
                    : userMsg.Content;
            }

            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, title));
        }
        catch
        {
            var fallback = userMsg.Content.Length > 40
                ? userMsg.Content[..40].TrimEnd() + "\u2026"
                : userMsg.Content;
            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, fallback));
        }
    }

    private async Task ApplyThemeAsync()
    {
        if (!_isInitialized) return;

        var isDark = IsDarkTheme();
        if (_rootGrid is not null)
            _rootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(isDark ? DarkBg : LightBg);
        await _renderer.SetThemeAsync(isDark);
    }

    private async Task ApplyLocaleStringsAsync()
    {
        if (!_isInitialized) return;

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");

            var strings = new Dictionary<string, string>
            {
                ["copy"] = loader.GetString("Chat_Copy"),
                ["copied"] = loader.GetString("Chat_Copied"),
                ["continue_label"] = loader.GetString("Chat_Continue"),
                ["code"] = loader.GetString("Chat_Code"),
                ["copyMessage"] = loader.GetString("Chat_CopyMessage"),
                ["edit"] = loader.GetString("Chat_Edit"),
                ["retry"] = loader.GetString("Chat_Retry"),
                ["summarize"] = loader.GetString("Chat_Summarize"),
                ["copyRequest"] = loader.GetString("Chat_CopyRequest"),
                ["copyResponse"] = loader.GetString("Chat_CopyResponse"),
                ["tokens"] = loader.GetString("Chat_Tokens")
            };

            // Filter out empty strings (key not found returns empty)
            var validStrings = strings
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            if (validStrings.Count > 0)
            {
                await _renderer.SetLocaleStringsAsync(validStrings);
            }
        }
        catch
        {
            // Resource loading may fail if no .resw files are available (consumer app).
            // Defaults in chat.html will be used.
        }
    }

    private AiRequest CreateRequest(IReadOnlyList<ChatMessage> messages, string? systemPrompt = null)
    {
        var preset = SelectedPreset;
        return new AiRequest
        {
            Messages = messages,
            SystemPrompt = systemPrompt ?? SystemPrompt,
            Temperature = preset?.Temperature ?? 0.7,
            MaxTokens = preset?.MaxTokens ?? 4096
        };
    }

    private bool IsDarkTheme() => Theme switch
    {
        ChatTheme.Light => false,
        ChatTheme.Dark => true,
        _ => ActualTheme == ElementTheme.Dark
    };
}
