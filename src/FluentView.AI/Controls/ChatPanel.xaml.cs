using System.Collections.ObjectModel;
using FluentView.AI.Models;
using FluentView.AI.Providers;
using FluentView.AI.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FluentView.AI.Controls;

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

    public event EventHandler<ChatMessage>? MessageAdded;

    public IReadOnlyList<ChatMessage> GetMessages() => _messages;

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

        var assistantMessage = new ChatMessage(ChatRole.Assistant) { IsStreaming = true };
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id);
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

    private bool IsDarkTheme()
    {
        return ActualTheme == ElementTheme.Dark;
    }
}
