using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentView.AI.Controls;
using FluentView.AI.Models;
using FluentView.AI.Providers;
using AssistView.Studio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistView.Studio.ViewModels;

public partial class ChatTabViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isDirty;

    public ChatPanel ChatPanel { get; }

    public IconSource? TabIconSource => IsDirty
        ? new FontIconSource
        {
            Glyph = "\uE915",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
        }
        : null;

    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(TabIconSource));
    }
    public bool HasBeenSaved { get; set; }
    public ProviderPreset? CurrentPreset { get; private set; }

    /// <summary>
    /// Raised when the user switches provider preset via InputContainer ComboBox.
    /// </summary>
    public event Action<ChatTabViewModel, ProviderPreset>? PresetSwitched;

    public ChatTabViewModel(
        ProviderPreset preset,
        string systemPrompt,
        ChatTheme theme,
        IList availablePresets,
        List<PromptPreset> promptPresets,
        PromptPreset? selectedPromptPreset)
    {
        CurrentPreset = preset;
        _title = preset.Name;

        var provider = ProviderFactory.Create(preset);

        ChatPanel = new ChatPanel
        {
            Provider = provider,
            SystemPrompt = systemPrompt,
            Theme = theme,
            AvailablePresets = availablePresets,
            SelectedPreset = preset,
            AvailablePromptPresets = promptPresets,
            SelectedPromptPreset = selectedPromptPreset,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            AutoTitle = AppSettings.UtilityAutoTitle,
#if DEBUG
            IsDebugMode = true,
#endif
        };

        ChatPanel.PresetChanged += OnPresetChanged;
        ChatPanel.TitleGenerated += OnTitleGenerated;
        ChatPanel.MessageAdded += OnMessageAdded;
    }

    public void AddRestoredMessage(ChatRole role, string content, string? providerName, string? providerModelId)
    {
        ChatPanel.AddRestoredMessage(role, content, providerName, providerModelId);
    }

    public IReadOnlyList<ChatMessage> GetMessages() => ChatPanel.GetMessages();

    public void ApplyTheme(ChatTheme theme) => ChatPanel.Theme = theme;

    public void ApplySystemPrompt(string prompt, List<PromptPreset> promptPresets, PromptPreset? selectedPromptPreset)
    {
        ChatPanel.SystemPrompt = prompt;
        ChatPanel.AvailablePromptPresets = promptPresets;
        ChatPanel.SelectedPromptPreset = selectedPromptPreset;
    }

    public void ApplyPromptPresets(List<PromptPreset> promptPresets, PromptPreset? selectedPromptPreset)
    {
        ChatPanel.AvailablePromptPresets = promptPresets;
        ChatPanel.SelectedPromptPreset = selectedPromptPreset;
    }

    public void ApplyPresets(IList presets)
    {
        var currentName = ChatPanel.SelectedPreset?.Name;
        ChatPanel.AvailablePresets = presets;

        if (currentName is not null)
        {
            foreach (ProviderPreset p in presets)
            {
                if (p.Name == currentName)
                {
                    ChatPanel.SelectedPreset = p;
                    break;
                }
            }
        }
    }

    private void OnPresetChanged(object? sender, ProviderPreset preset)
    {
        // Dispose old provider
        if (ChatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Create new provider — conversation history is preserved
        ChatPanel.Provider = ProviderFactory.Create(preset);
        CurrentPreset = preset;
        Title = preset.Name;

        PresetSwitched?.Invoke(this, preset);
    }

    private void OnTitleGenerated(object? sender, string title)
    {
        Title = title;
    }

    private void OnMessageAdded(object? sender, ChatMessage message)
    {
        IsDirty = true;
    }

    public void Dispose()
    {
        ChatPanel.PresetChanged -= OnPresetChanged;
        ChatPanel.TitleGenerated -= OnTitleGenerated;
        ChatPanel.MessageAdded -= OnMessageAdded;

        if (ChatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
