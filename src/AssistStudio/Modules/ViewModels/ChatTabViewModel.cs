using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AssistStudio.ViewModels;

/// <summary>
/// View model for a single conversation tab, managing the chat panel, provider preset,
/// dirty state, and file association.
/// </summary>
public partial class ChatTabViewModel : ObservableObject, IDisposable
{
    #region Observable Fields

    /// <summary>
    /// The display title for this conversation tab.
    /// </summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>
    /// The header text shown on the tab strip.
    /// </summary>
    [ObservableProperty] private string _tabHeader = string.Empty;

    /// <summary>
    /// Indicates whether the conversation has unsaved changes.
    /// </summary>
    [ObservableProperty] private bool _isDirty;

    /// <summary>
    /// The file path where this conversation is saved, or <c>null</c> if unsaved.
    /// </summary>
    [ObservableProperty] private string? _filePath;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the chat panel control that hosts the conversation UI and provider interaction.
    /// </summary>
    public ChatPanel ChatPanel { get; }

    /// <summary>
    /// Gets the icon source for the tab, showing a dot indicator when dirty.
    /// </summary>
    public IconSource? TabIconSource => IsDirty
        ? new FontIconSource
        {
            Glyph = "\uE915",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"],
        }
        : null;

    /// <summary>
    /// Gets or sets whether this conversation has been saved at least once.
    /// </summary>
    public bool HasBeenSaved { get; set; }

    /// <summary>
    /// Gets the currently active provider preset for this tab.
    /// </summary>
    public ProviderPreset? CurrentPreset { get; private set; }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user switches provider preset via InputContainer ComboBox.
    /// </summary>
    public event Action<ChatTabViewModel, ProviderPreset>? PresetSwitched;

    #endregion

    #region Observable Property Changed Handlers

    /// <summary>
    /// Notifies the UI that the tab icon source may have changed when dirty state changes.
    /// </summary>
    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(TabIconSource));
    }

    /// <summary>
    /// Propagates title changes to the underlying chat panel.
    /// </summary>
    partial void OnTitleChanged(string value)
    {
        ChatPanel.Title = value;
    }

    /// <summary>
    /// Updates the tab header to the file name when a file path is assigned.
    /// </summary>
    partial void OnFilePathChanged(string? value)
    {
        if (value is not null)
            TabHeader = Path.GetFileNameWithoutExtension(value);
    }

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatTabViewModel"/> class with the specified
    /// preset, system prompt, theme, and available presets.
    /// </summary>
    public ChatTabViewModel(
        ProviderPreset preset,
        string systemPrompt,
        ChatTheme theme,
        IList availablePresets,
        List<PromptPreset> promptPresets,
        PromptPreset? selectedPromptPreset,
        int tabNumber = 0)
    {
        CurrentPreset = preset;
        _title = preset.Name;

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var prefix = loader.GetString("Tab_NewConversation");
        _tabHeader = tabNumber > 0 ? $"{prefix} {tabNumber}" : prefix;

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

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a restored message to the chat panel during conversation loading.
    /// </summary>
    public void AddRestoredMessage(ChatRole role, string content, string? providerName, string? providerModelId)
    {
        ChatPanel.AddRestoredMessage(role, content, providerName, providerModelId);
    }

    /// <summary>
    /// Gets the list of chat messages in this conversation.
    /// </summary>
    /// <returns>A read-only list of chat messages.</returns>
    public IReadOnlyList<ChatMessage> GetMessages() => ChatPanel.GetMessages();

    /// <summary>
    /// Applies a visual theme to the chat panel.
    /// </summary>
    public void ApplyTheme(ChatTheme theme) => ChatPanel.Theme = theme;

    /// <summary>
    /// Updates the system prompt and prompt presets on the chat panel.
    /// </summary>
    public void ApplySystemPrompt(string prompt, List<PromptPreset> promptPresets, PromptPreset? selectedPromptPreset)
    {
        ChatPanel.SystemPrompt = prompt;
        ChatPanel.AvailablePromptPresets = promptPresets;
        ChatPanel.SelectedPromptPreset = selectedPromptPreset;
    }

    /// <summary>
    /// Updates the available prompt presets and selected preset on the chat panel.
    /// </summary>
    public void ApplyPromptPresets(List<PromptPreset> promptPresets, PromptPreset? selectedPromptPreset)
    {
        ChatPanel.AvailablePromptPresets = promptPresets;
        ChatPanel.SelectedPromptPreset = selectedPromptPreset;
    }

    /// <summary>
    /// Updates the available provider presets on the chat panel, preserving the current selection.
    /// </summary>
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

    /// <inheritdoc/>
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

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles provider preset changes by disposing the old provider and creating a new one.
    /// </summary>
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

    /// <summary>
    /// Handles the auto-generated title from the utility AI and applies it to the tab.
    /// </summary>
    private void OnTitleGenerated(object? sender, string title)
    {
        Title = title;
    }

    /// <summary>
    /// Handles the user requesting to edit the conversation title via a rename dialog.
    /// </summary>
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

    /// <summary>
    /// Marks the conversation as dirty when a new message is added.
    /// </summary>
    private void OnMessageAdded(object? sender, ChatMessage message)
    {
        IsDirty = true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Resolves the utility AI provider based on the user's settings, returning <c>null</c>
    /// if the source is not set to "Specific" or no matching preset is found.
    /// </summary>
    /// <returns>The utility AI provider, or <c>null</c> if not configured.</returns>
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

    #endregion
}
