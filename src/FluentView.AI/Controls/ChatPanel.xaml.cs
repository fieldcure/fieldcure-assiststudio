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

    public ChatPanel()
    {
        InitializeComponent();
        InputArea.MessageSent += OnMessageSent;
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

    private async void OnMessageSent(object? sender, string text)
    {
        if (!_isInitialized || string.IsNullOrWhiteSpace(text)) return;

        // Add user message
        var userMessage = new ChatMessage(ChatRole.User, text);
        _messages.Add(userMessage);
        await _renderer.AppendUserMessageAsync(userMessage.Id, userMessage.Content, userMessage.Timestamp.ToString("O"));
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

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
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
