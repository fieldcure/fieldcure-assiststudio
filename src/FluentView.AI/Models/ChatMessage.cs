using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FluentView.AI.Models;

public enum ChatRole
{
    User,
    Assistant,
    System
}

public partial class ChatMessage : INotifyPropertyChanged
{
    private string _content;
    private bool _isStreaming;

    public ChatMessage(ChatRole role, string content = "")
    {
        Id = Guid.NewGuid().ToString("N");
        Role = role;
        _content = content;
        Timestamp = DateTime.UtcNow;
        Attachments = [];
    }

    public string Id { get; }
    public ChatRole Role { get; }

    public string Content
    {
        get => _content;
        set => SetField(ref _content, value);
    }

    public bool IsStreaming
    {
        get => _isStreaming;
        set => SetField(ref _isStreaming, value);
    }

    public DateTime Timestamp { get; }
    public IReadOnlyList<ChatAttachment> Attachments { get; init; }

    /// <summary>
    /// Provider name that generated this message (e.g., "Claude", "OpenAI").
    /// </summary>
    public string? ProviderName { get; init; }

    /// <summary>
    /// Model ID used to generate this message (e.g., "claude-sonnet-4-20250514").
    /// </summary>
    public string? ProviderModelId { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
