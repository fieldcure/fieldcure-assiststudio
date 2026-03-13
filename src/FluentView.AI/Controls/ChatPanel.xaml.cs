using System.Collections;
using System.Collections.ObjectModel;
using FluentView.AI.Models;
using FluentView.AI.Providers;
using FluentView.AI.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FluentView.AI.Controls;

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

public sealed partial class ChatPanel : UserControl
{
    public static readonly DependencyProperty ProviderProperty =
        DependencyProperty.Register(nameof(Provider), typeof(IAiProvider), typeof(ChatPanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(ChatPanel),
            new PropertyMetadata("Type a message..."));

    public static readonly DependencyProperty SystemPromptProperty =
        DependencyProperty.Register(nameof(SystemPrompt), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null));

    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(ChatTheme), typeof(ChatPanel),
            new PropertyMetadata(ChatTheme.System, OnThemePropertyChanged));

    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    public static readonly DependencyProperty AvailablePromptPresetsProperty =
        DependencyProperty.Register(nameof(AvailablePromptPresets), typeof(IList<PromptPreset>), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailablePromptPresetsChanged));

    public static readonly DependencyProperty SelectedPromptPresetProperty =
        DependencyProperty.Register(nameof(SelectedPromptPreset), typeof(PromptPreset), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedPromptPresetChanged));

    private static void OnThemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized)
        {
            _ = panel.ApplyThemeAsync();
        }
    }

    private readonly WebViewChatRenderer _renderer = new();
    private readonly ObservableCollection<ChatMessage> _messages = [];
    private CancellationTokenSource? _streamingCts;
    private bool _isInitialized;

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

    public ChatPanel()
    {
        InitializeComponent();
        // Set initial background to match CSS --bg-primary (before WebView2 loads)
        RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(LightBg);
        InputArea.MessageSent += OnMessageSent;
        InputArea.PresetChanged += OnInputPresetChanged;
        InputArea.PromptPresetChanged += OnInputPromptPresetChanged;
        _renderer.ContinueRequested += OnContinueRequested;
        _renderer.MessageCopyRequested += OnMessageCopyRequested;
        _renderer.RetryRequested += OnRetryRequested;
        _renderer.EditRequested += OnEditRequested;
        _renderer.SummarizeRequested += OnSummarizeRequested;
    }

    public IAiProvider? Provider
    {
        get => (IAiProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

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

    public IList? AvailablePresets
    {
        get => (IList?)GetValue(AvailablePresetsProperty);
        set => SetValue(AvailablePresetsProperty, value);
    }

    public ProviderPreset? SelectedPreset
    {
        get => (ProviderPreset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    public IList<PromptPreset>? AvailablePromptPresets
    {
        get => (IList<PromptPreset>?)GetValue(AvailablePromptPresetsProperty);
        set => SetValue(AvailablePromptPresetsProperty, value);
    }

    public PromptPreset? SelectedPromptPreset
    {
        get => (PromptPreset?)GetValue(SelectedPromptPresetProperty);
        set => SetValue(SelectedPromptPresetProperty, value);
    }

    public event EventHandler<ProviderPreset>? PresetChanged;
    public event EventHandler<ChatMessage>? MessageAdded;

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

    public async void ClearConversation()
    {
        _streamingCts?.Cancel();
        _messages.Clear();
        if (_isInitialized)
        {
            await _renderer.SetThemeAsync(IsDarkTheme());
            await _renderer.InitializeAsync(ChatWebView);
            await ApplyThemeAsync();
        }

        // Switch back to empty state
        if (ChatLayout.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            ChatLayout.Children.Remove(InputArea);
            InputArea.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            EmptyStatePanel.Children.Add(InputArea);
            ChatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            EmptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            await _renderer.InitializeAsync(ChatWebView);
            _isInitialized = true;
            await ApplyThemeAsync();
            await ApplyLocaleStringsAsync();

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
        if (ChatLayout.Visibility == Microsoft.UI.Xaml.Visibility.Visible) return;

        EmptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        ChatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        // Move InputArea from EmptyStatePanel into ChatLayout as Row 1
        EmptyStatePanel.Children.Remove(InputArea);
        InputArea.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        Grid.SetRow(InputArea, 1);
        ChatLayout.Children.Add(InputArea);
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

        InputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = new AiRequest
            {
                Messages = _messages.ToList(),
                SystemPrompt = SystemPrompt
            };

            await foreach (var token in Provider.StreamAsync(request, ct))
            {
                assistantMessage.Content += token;
                await _renderer.AppendTokenAsync(assistantMessage.Id, token);
            }

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated, Provider.LastUsage?.TotalTokens ?? 0);

            // Auto-summarize if enabled and token threshold exceeded
            if (AutoSummarize && MaxInputTokens > 0 && Provider.LastUsage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await SummarizeHistoryAsync(ct);
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
            InputArea.IsInputEnabled = true;
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

        InputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = new AiRequest
            {
                Messages = _messages.ToList(),
                SystemPrompt = SystemPrompt
            };

            var continuationText = "";
            await foreach (var token in Provider.StreamAsync(request, ct))
            {
                continuationText += token;
                await _renderer.AppendTokenAsync(assistantMessage.Id, token);
            }

            // Append continuation to the original assistant message content
            assistantMessage.Content += continuationText;

            // Remove the continue user message from history — replace with combined assistant content
            _messages.Remove(continueMessage);

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated, Provider.LastUsage?.TotalTokens ?? 0);
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
            InputArea.IsInputEnabled = true;
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
            for (int i = 0; i < oldMessages.Count; i++)
            {
                _messages.RemoveAt(0);
            }

            _messages.Insert(0, new ChatMessage(ChatRole.System,
                $"[Previous conversation summary]\n{summary}"));
        }
        catch
        {
            // Summarization failed — keep messages as-is
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
        var escaped = System.Text.Json.JsonSerializer.Serialize(e.NewText);
        await ChatWebView.ExecuteScriptAsync(
            $"document.querySelector('#msg-{e.MessageId} .message-bubble').textContent = {escaped}");

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

        InputArea.IsInputEnabled = false;
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
            InputArea.IsInputEnabled = true;
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

        InputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = new AiRequest
            {
                Messages = _messages.ToList(),
                SystemPrompt = SystemPrompt
            };

            await foreach (var token in Provider.StreamAsync(request, ct))
            {
                assistantMessage.Content += token;
                await _renderer.AppendTokenAsync(assistantMessage.Id, token);
            }

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated, Provider.LastUsage?.TotalTokens ?? 0);

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
            InputArea.IsInputEnabled = true;
        }
    }

    private void OnInputPresetChanged(object? sender, ProviderPreset preset)
    {
        SelectedPreset = preset;
        PresetChanged?.Invoke(this, preset);
    }

    private static void OnAvailablePresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
        {
            panel.InputArea.AvailablePresets = e.NewValue as IList;
        }
    }

    private static void OnSelectedPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is ProviderPreset preset)
        {
            panel.InputArea.SelectedPreset = preset;
            panel.UpdatePlaceholderWithProvider(preset.Name);
        }
    }

    private void UpdatePlaceholderWithProvider(string providerName)
    {
        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader("FluentView.AI/Resources");
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
            panel.InputArea.AvailablePromptPresets = presets;
        }
    }

    private static void OnSelectedPromptPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is PromptPreset preset)
        {
            panel.InputArea.SelectedPromptPreset = preset;
            panel.InputArea.SelectPromptPresetInCombo(preset);
            // Update the actual system prompt used in requests
            panel.SystemPrompt = preset.Text;
        }
    }

    private void OnInputPromptPresetChanged(object? sender, PromptPreset preset)
    {
        SelectedPromptPreset = preset;
        SystemPrompt = preset.Text;
    }

    // Background colors matching chat.html --bg-primary (opaque, no alpha issues)
    private static readonly Windows.UI.Color LightBg = Windows.UI.Color.FromArgb(255, 0xF5, 0xF5, 0xF5);
    private static readonly Windows.UI.Color DarkBg = Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);

    private async Task ApplyThemeAsync()
    {
        if (!_isInitialized) return;

        var isDark = IsDarkTheme();
        RootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(isDark ? DarkBg : LightBg);
        await _renderer.SetThemeAsync(isDark);
    }

    private async Task ApplyLocaleStringsAsync()
    {
        if (!_isInitialized) return;

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "FluentView.AI/Resources");

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

    private bool IsDarkTheme() => Theme switch
    {
        ChatTheme.Light => false,
        ChatTheme.Dark => true,
        _ => ActualTheme == ElementTheme.Dark
    };
}
