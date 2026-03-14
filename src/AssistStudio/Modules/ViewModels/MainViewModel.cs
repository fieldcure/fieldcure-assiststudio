using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using AssistStudio.Helpers;

namespace AssistStudio.ViewModels;

/// <summary>
/// Top-level view model that manages the collection of conversation tabs and
/// propagates settings changes across all open tabs.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    #region Observable Fields

    /// <summary>
    /// The currently selected conversation tab.
    /// </summary>
    [ObservableProperty] private ChatTabViewModel? _selectedTab;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the observable collection of open conversation tabs.
    /// </summary>
    public ObservableCollection<ChatTabViewModel> Tabs { get; } = [];

    /// <summary>
    /// Provides the current preset list from SettingsPanel.
    /// Set by MainWindow after construction.
    /// </summary>
    public Func<IList> GetPresets { get; set; } = () => new List<ProviderPreset>();

    #endregion

    #region Fields

    /// <summary>
    /// Counter used to assign sequential numbers to new tabs.
    /// </summary>
    private int _tabCounter;

    /// <summary>
    /// Cached list of prompt presets loaded from settings.
    /// </summary>
    private List<PromptPreset> _promptPresets;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class and loads prompt presets.
    /// </summary>
    public MainViewModel()
    {
        _promptPresets = AppSettings.LoadPromptPresets();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Creates and adds a new conversation tab with the specified or default provider preset.
    /// </summary>
    /// <returns>The newly created tab view model.</returns>
    public ChatTabViewModel AddTab(ProviderPreset? preset = null)
    {
        _tabCounter++;
        preset ??= GetDefaultPreset();

        var vm = new ChatTabViewModel(
            preset,
            GetActivePromptText(),
            GetCurrentTheme(),
            GetPresets(),
            _promptPresets,
            GetActivePromptPreset(),
            _tabCounter);

        Tabs.Add(vm);
        SelectedTab = vm;
        return vm;
    }

    /// <summary>
    /// Loads a saved conversation into a new tab with restored messages and metadata.
    /// </summary>
    /// <returns>The tab view model containing the loaded conversation.</returns>
    public ChatTabViewModel LoadConversation(ConversationData data, string? filePath = null)
    {
        _tabCounter++;

        // Find matching preset or use default
        ProviderPreset? preset = null;
        var presets = GetPresets();
        if (data.ProviderPresetName is not null)
        {
            foreach (ProviderPreset p in presets)
            {
                if (p.Name == data.ProviderPresetName)
                {
                    preset = p;
                    break;
                }
            }
        }
        preset ??= GetDefaultPreset();

        var vm = new ChatTabViewModel(
            preset,
            GetActivePromptText(),
            GetCurrentTheme(),
            presets,
            _promptPresets,
            GetActivePromptPreset());

        // Restore messages
        foreach (var msg in data.Messages)
        {
            vm.AddRestoredMessage(msg.Role, msg.Content, msg.ProviderName, msg.ProviderModelId);
        }

        vm.Title = data.TabName;
        vm.HasBeenSaved = true;
        vm.FilePath = filePath;

        Tabs.Add(vm);
        SelectedTab = vm;
        return vm;
    }

    /// <summary>
    /// Saves a single tab's conversation to its file path or to the default conversation store.
    /// </summary>
    public async Task SaveTabAsync(ChatTabViewModel? tab)
    {
        if (tab is null) return;

        var messages = tab.GetMessages();
        if (messages.Count == 0) return;

        var tabName = tab.Title;
        var presetName = tab.CurrentPreset?.Name;

        try
        {
            if (tab.FilePath is not null)
                await ConversationManager.SaveToFileAsync(tab.FilePath, tabName, presetName, messages);
            else
                await ConversationManager.SaveConversationAsync(tabName, presetName, messages);
            tab.IsDirty = false;
        }
        catch { /* Save failed silently */ }
    }

    /// <summary>
    /// Saves all open tabs' conversations.
    /// </summary>
    public async Task SaveAllAsync()
    {
        foreach (var tab in Tabs)
        {
            await SaveTabAsync(tab);
        }
    }

    /// <summary>
    /// Disposes and removes the specified tab from the collection.
    /// </summary>
    public void CloseTab(ChatTabViewModel tab)
    {
        tab.Dispose();
        Tabs.Remove(tab);
    }

    #endregion

    #region Settings Propagation

    /// <summary>
    /// Applies the specified theme to all open conversation tabs.
    /// </summary>
    public void ApplyThemeToAll(string theme)
    {
        var chatTheme = theme switch
        {
            "Light" => ChatTheme.Light,
            "Dark" => ChatTheme.Dark,
            _ => ChatTheme.System,
        };

        foreach (var tab in Tabs)
        {
            tab.ApplyTheme(chatTheme);
        }
    }

    /// <summary>
    /// Updates the system prompt on all open conversation tabs and reloads prompt presets.
    /// </summary>
    public void ApplySystemPromptToAll(string prompt)
    {
        _promptPresets = AppSettings.LoadPromptPresets();
        var active = GetActivePromptPreset();

        foreach (var tab in Tabs)
        {
            tab.ApplySystemPrompt(prompt, _promptPresets, active);
        }
    }

    /// <summary>
    /// Refreshes prompt presets on all open conversation tabs after a preset change.
    /// </summary>
    public void RefreshPromptPresetsOnAll()
    {
        _promptPresets = AppSettings.LoadPromptPresets();
        var active = GetActivePromptPreset();

        foreach (var tab in Tabs)
        {
            tab.ApplyPromptPresets(_promptPresets, active);
        }
    }

    /// <summary>
    /// Refreshes provider presets on all open conversation tabs after a preset change.
    /// </summary>
    public void RefreshPresetsOnAll()
    {
        var presets = GetPresets();
        foreach (var tab in Tabs)
        {
            tab.ApplyPresets(presets);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Gets the default provider preset based on the user's saved preference.
    /// Falls back to Mock if no presets are available or the saved default is not found.
    /// </summary>
    /// <returns>The default provider preset.</returns>
    private ProviderPreset GetDefaultPreset()
    {
        var presets = GetPresets();
        if (presets.Count == 0)
            return new ProviderPreset { Name = "Mock", ProviderType = "Mock" };

        var defaultName = AppSettings.DefaultProvider;
        foreach (ProviderPreset p in presets)
        {
            if (p.Name == defaultName) return p;
        }
        return (ProviderPreset)presets[0]!;
    }

    /// <summary>
    /// Converts the current theme setting string to a <see cref="ChatTheme"/> enum value.
    /// </summary>
    /// <returns>The corresponding chat theme.</returns>
    private static ChatTheme GetCurrentTheme()
    {
        return AppSettings.Theme switch
        {
            "Light" => ChatTheme.Light,
            "Dark" => ChatTheme.Dark,
            _ => ChatTheme.System,
        };
    }

    /// <summary>
    /// Finds the currently active prompt preset by name, falling back to the first preset.
    /// </summary>
    /// <returns>The active prompt preset, or <c>null</c> if none exist.</returns>
    private PromptPreset? GetActivePromptPreset()
    {
        var name = AppSettings.ActivePromptPreset;
        return _promptPresets.Find(p => p.Name == name) ?? _promptPresets.FirstOrDefault();
    }

    /// <summary>
    /// Gets the text of the active prompt preset, falling back to the stored system prompt.
    /// </summary>
    /// <returns>The system prompt text to use for new conversations.</returns>
    private string GetActivePromptText()
    {
        return GetActivePromptPreset()?.Text ?? AppSettings.SystemPrompt;
    }

    #endregion
}
