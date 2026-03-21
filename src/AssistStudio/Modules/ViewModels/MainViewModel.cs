using AssistStudio.Helpers;
using AssistStudio.Models;
using AssistStudio.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Tools;
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
        ToolRegistry.Register(new ReadFileTool());
        ToolRegistry.Register(new WriteFileTool());
        ToolRegistry.Register(new SearchFilesTool());
        ToolRegistry.Register(new RunCommandTool());
        ToolRegistry.Register(new UrlFetchTool());
        ToolRegistry.Register(new SearchToolsTool(App.McpRegistry));
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
        LoggingService.LogInfo($"[Tab] Created tab #{_tabCounter}: preset={preset.Name}");
        return vm;
    }

    /// <summary>
    /// Loads a saved conversation into a tab. If the conversation is already open in an
    /// existing tab, switches to that tab. If the currently selected tab is empty (new and
    /// unused), reuses it instead of creating a new one.
    /// </summary>
    /// <returns>The tab view model containing the loaded conversation.</returns>
    public ChatTabViewModel LoadConversation(ConversationData data, string? filePath = null)
    {
        // If this file is already open in a tab, just switch to it
        if (filePath is not null)
        {
            var existing = FindTabByFilePath(filePath);
            if (existing is not null)
            {
                LoggingService.LogInfo($"[Tab] Switched to existing tab: {Path.GetFileName(filePath)}");
                SelectedTab = existing;
                return existing;
            }
        }

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

        // Reuse the current tab if it is empty (no messages, not dirty, never saved)
        var reuseTab = SelectedTab is not null && IsTabEmpty(SelectedTab);

        ChatTabViewModel vm;
        if (reuseTab)
        {
            // Dispose the empty tab and replace it at the same index
            var idx = Tabs.IndexOf(SelectedTab!);
            var oldTab = SelectedTab!;
            oldTab.Dispose();

            vm = new ChatTabViewModel(
                preset,
                GetActivePromptText(),
                GetCurrentTheme(),
                presets,
                _profiles,
                GetActiveProfile());

            Tabs[idx] = vm;
        }
        else
        {
            _tabCounter++;
            vm = new ChatTabViewModel(
                preset,
                GetActivePromptText(),
                GetCurrentTheme(),
                presets,
                _profiles,
                GetActiveProfile());

            Tabs.Add(vm);
        }

        // Restore messages with tree reconstruction
        RestoreConversationTree(vm, data.Messages);

        vm.Title = data.TabName;
        vm.HasBeenSaved = true;
        vm.FilePath = filePath;

        SelectedTab = vm;
        LoggingService.LogInfo($"[Tab] Loaded conversation: {data.TabName}, messages={data.Messages.Count}, reused={reuseTab}");
        return vm;
    }

    /// <summary>
    /// Saves a single tab's conversation to its file path or to the default conversation store.
    /// </summary>
    public static async Task SaveTabAsync(ChatTabViewModel? tab)
    {
        if (tab is null) return;

        var messages = tab.GetAllMessages();
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
        catch (Exception ex) { LoggingService.LogException(ex); }
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
        LoggingService.LogInfo($"[Tab] Closed: {tab.Title}");
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
        LoggingService.LogInfo($"[Settings] Applying theme '{theme}' to {Tabs.Count} tabs");
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
        LoggingService.LogInfo($"[Settings] Applying system prompt to {Tabs.Count} tabs, activeProfile={active?.Name}");

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
        LoggingService.LogInfo($"[Settings] Refreshing profiles: {_profiles.Count} profiles, active={active?.Name}");

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
        LoggingService.LogInfo($"[Settings] Refreshing presets: {presets.Count} presets on {Tabs.Count} tabs");
        foreach (var tab in Tabs)
        {
            tab.ApplyPresets(presets);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Restores a conversation tree from saved messages.
    /// Determines the active path (root → last leaf) and registers branch messages in the tree only.
    /// </summary>
    private static void RestoreConversationTree(ChatTabViewModel vm, List<SavedMessage> messages)
    {
        if (messages.Count == 0) return;

        // Check if any message has branching (multiple children for same parent)
        var hasBranching = messages
            .GroupBy(m => m.ParentId ?? "")
            .Any(g => g.Count() > 1);

        if (!hasBranching)
        {
            // Simple linear conversation — add all as active path
            foreach (var msg in messages)
                vm.AddRestoredMessage(msg.Role, msg.Content, msg.ProviderName, msg.ProviderModelId, msg.Id, msg.ParentId, msg.ToolCalls, msg.ToolCallId);
            return;
        }

        // Build parent→children map to find the active path
        var childrenMap = new Dictionary<string, List<SavedMessage>>();
        var rootKey = "";
        foreach (var msg in messages)
        {
            var key = msg.ParentId ?? rootKey;
            if (!childrenMap.TryGetValue(key, out var list))
            {
                list = [];
                childrenMap[key] = list;
            }
            list.Add(msg);
        }

        // Walk from root following last child (most recent branch) to build active path
        var activePath = new HashSet<string?>();
        var currentKey = rootKey;
        while (childrenMap.TryGetValue(currentKey, out var children) && children.Count > 0)
        {
            var last = children[^1]; // most recent branch
            activePath.Add(last.Id);
            currentKey = last.Id ?? rootKey;
        }

        // First pass: register branch-only messages in the tree (not on the active path)
        foreach (var msg in messages.Where(m => !activePath.Contains(m.Id)))
            vm.RegisterBranchMessage(msg.Role, msg.Content, msg.ProviderName, msg.ProviderModelId, msg.Id, msg.ParentId, msg.ToolCalls, msg.ToolCallId);

        // Second pass: add active path messages (registers in tree + adds to _messages)
        foreach (var msg in messages.Where(m => activePath.Contains(m.Id)))
            vm.AddRestoredMessage(msg.Role, msg.Content, msg.ProviderName, msg.ProviderModelId, msg.Id, msg.ParentId, msg.ToolCalls, msg.ToolCallId);
    }

    /// <summary>
    /// Finds an already-open tab whose <see cref="ChatTabViewModel.FilePath"/> matches
    /// the given path (case-insensitive on Windows).
    /// </summary>
    private ChatTabViewModel? FindTabByFilePath(string filePath)
    {
        foreach (var tab in Tabs)
        {
            if (tab.FilePath is not null &&
                string.Equals(tab.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns <c>true</c> if the tab has no messages, is not dirty, and has never been saved —
    /// i.e., it is a freshly created, unused tab.
    /// </summary>
    private static bool IsTabEmpty(ChatTabViewModel tab)
    {
        return tab.GetMessages().Count == 0 && !tab.IsDirty && !tab.HasBeenSaved;
    }

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

    /// <summary>
    /// Called when the selected tab changes. Focuses the input text box of the new tab.
    /// </summary>
    partial void OnSelectedTabChanged(ChatTabViewModel? value)
    {
        if (value is null) return;

        if (value.Panel is not null)
        {
            value.Panel.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => value.Panel?.FocusInput());
        }
        else
        {
            value.FocusPendingOnAttach = true;
        }
    }

    #endregion
}
