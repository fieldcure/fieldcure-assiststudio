using AssistStudio.Modules.Helpers;
using AssistStudio.Modules.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using System.Collections;
using System.Collections.ObjectModel;

namespace AssistStudio.Modules.ViewModels;

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
    /// Cached list of profiles loaded from settings.
    /// </summary>
    private List<Profile> _profiles;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class and loads profiles.
    /// </summary>
    public MainViewModel()
    {
        _profiles = AppSettings.LoadProfiles();

        // Register available tools
        ToolRegistry.Register(new ScanDirectoryTool());
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
            _profiles,
            GetActiveProfile(),
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
            _profiles,
            GetActiveProfile());

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
    /// Updates the system prompt on all open conversation tabs and reloads profiles.
    /// </summary>
    public void ApplySystemPromptToAll(string prompt)
    {
        _profiles = AppSettings.LoadProfiles();
        var active = GetActiveProfile();

        foreach (var tab in Tabs)
        {
            tab.ApplySystemPrompt(prompt, _profiles, active);
        }
    }

    /// <summary>
    /// Refreshes profiles on all open conversation tabs after a profile change.
    /// </summary>
    public void RefreshProfilesOnAll()
    {
        _profiles = AppSettings.LoadProfiles();
        var active = GetActiveProfile();

        foreach (var tab in Tabs)
        {
            tab.ApplyProfiles(_profiles, active);
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
    /// Gets the default provider preset based on the active profile's preferred provider type.
    /// Falls back to the first available preset, or Mock if none exist.
    /// </summary>
    /// <returns>The default provider preset.</returns>
    private ProviderPreset GetDefaultPreset()
    {
        var presets = GetPresets();
        if (presets.Count == 0)
            return new ProviderPreset { Name = "Mock", ProviderType = "Mock" };

        var preferredType = GetActiveProfile()?.PreferredProviderType;
        if (preferredType is not null)
        {
            foreach (ProviderPreset p in presets)
            {
                if (p.ProviderType == preferredType) return p;
            }
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
    /// Finds the currently active profile by name, falling back to the first profile.
    /// </summary>
    /// <returns>The active profile, or <c>null</c> if none exist.</returns>
    private Profile? GetActiveProfile()
    {
        var name = AppSettings.ActiveProfile;
        return _profiles.Find(p => p.Name == name) ?? _profiles.FirstOrDefault();
    }

    /// <summary>
    /// Gets the text of the active profile, falling back to the stored system prompt.
    /// </summary>
    /// <returns>The system prompt text to use for new conversations.</returns>
    private string GetActivePromptText()
    {
        return GetActiveProfile()?.Text ?? AppSettings.SystemPrompt;
    }

    #endregion
}
