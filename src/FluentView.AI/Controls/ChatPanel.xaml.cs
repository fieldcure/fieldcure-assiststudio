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
        InputArea.MessageSent += OnMessageSent;
        _renderer.ContinueRequested += OnContinueRequested;
        _renderer.MessageCopyRequested += OnMessageCopyRequested;
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
            // Re-initialize to clear the WebView content
            await _renderer.InitializeAsync(ChatWebView);
            await ApplyThemeAsync();
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

    private async void OnMessageSent(object? sender, MessageSentEventArgs e)
    {
        if (!_isInitialized) return;
        if (string.IsNullOrWhiteSpace(e.Text) && e.Attachments.Count == 0) return;

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

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated);

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

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, Provider.IsTruncated);
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

    private async Task ApplyThemeAsync()
    {
        if (_isInitialized)
        {
            await _renderer.SetThemeAsync(IsDarkTheme());
        }
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
                ["copyMessage"] = loader.GetString("Chat_CopyMessage")
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
