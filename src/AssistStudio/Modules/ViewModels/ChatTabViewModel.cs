using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentView.AI.Controls;
using FluentView.AI.Models;
using FluentView.AI.Providers;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.ViewModels;

public partial class ChatTabViewModel : ObservableObject, IDisposable
{
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _tabHeader = string.Empty;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private string? _filePath;

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

    partial void OnTitleChanged(string value)
    {
        ChatPanel.Title = value;
    }

    partial void OnFilePathChanged(string? value)
    {
        if (value is not null)
            TabHeader = Path.GetFileNameWithoutExtension(value);
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

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        _tabHeader = loader.GetString("Tab_NewConversation");

        var provider = ProviderFactory.Create(preset);

        ChatPanel = new ChatPanel
        {
            Provider = provider,
            UtilityProvider = ResolveUtilityProvider(availablePresets),
            SystemPrompt = systemPrompt,
            Theme = theme,
            AvailablePresets = availablePresets,
            SelectedPreset = preset,
            AvailablePromptPresets = promptPresets,
            SelectedPromptPreset = selectedPromptPreset,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            AutoTitle = AppSettings.UtilityAutoTitle,
            AutoSummarize = AppSettings.UtilityAutoSummary,
#if DEBUG
            IsDebugMode = true,
#endif
        };

        ChatPanel.PresetChanged += OnPresetChanged;
        ChatPanel.TitleGenerated += OnTitleGenerated;
        ChatPanel.MessageAdded += OnMessageAdded;
        ChatPanel.TitleEditRequested += OnTitleEditRequested;
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

    private async void OnTitleEditRequested(object? sender, string currentTitle)
    {
        var input = new TextBox { Text = currentTitle, SelectionStart = currentTitle.Length };
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var dialog = new ContentDialog
        {
            Title = loader.GetString("Dialog_RenameConversation"),
            Content = input,
            PrimaryButtonText = loader.GetString("Dialog_OK"),
            CloseButtonText = loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = ChatPanel.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            Title = input.Text.Trim();
        }
    }

    private void OnMessageAdded(object? sender, ChatMessage message)
    {
        IsDirty = true;
    }

    private static IAiProvider? ResolveUtilityProvider(IList availablePresets)
    {
        if (AppSettings.UtilityAISource != "Specific") return null;

        var presetName = AppSettings.UtilityAIPreset;
        if (string.IsNullOrEmpty(presetName)) return null;

        foreach (ProviderPreset p in availablePresets)
        {
            if (p.Name == presetName)
                return ProviderFactory.Create(p);
        }
        return null;
    }

    public void Dispose()
    {
        ChatPanel.PresetChanged -= OnPresetChanged;
        ChatPanel.TitleGenerated -= OnTitleGenerated;
        ChatPanel.MessageAdded -= OnMessageAdded;
        ChatPanel.TitleEditRequested -= OnTitleEditRequested;

        if (ChatPanel.UtilityProvider is IDisposable utilDisposable)
        {
            utilDisposable.Dispose();
        }
        if (ChatPanel.Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
