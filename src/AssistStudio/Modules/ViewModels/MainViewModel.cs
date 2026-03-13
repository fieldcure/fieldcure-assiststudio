using System.Collections;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentView.AI.Controls;
using FluentView.AI.Helpers;
using FluentView.AI.Models;
using AssistStudio.Helpers;

namespace AssistStudio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private ChatTabViewModel? _selectedTab;

    public ObservableCollection<ChatTabViewModel> Tabs { get; } = [];

    private int _tabCounter;
    private List<PromptPreset> _promptPresets;

    /// <summary>
    /// Provides the current preset list from SettingsPanel.
    /// Set by MainWindow after construction.
    /// </summary>
    public Func<IList> GetPresets { get; set; } = () => new List<ProviderPreset>();

    public MainViewModel()
    {
        _promptPresets = AppSettings.LoadPromptPresets();
    }

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
            GetActivePromptPreset());

        Tabs.Add(vm);
        SelectedTab = vm;
        return vm;
    }

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

    public async Task SaveAllAsync()
    {
        foreach (var tab in Tabs)
        {
            await SaveTabAsync(tab);
        }
    }

    public void CloseTab(ChatTabViewModel tab)
    {
        tab.Dispose();
        Tabs.Remove(tab);
    }

    // ===== Settings Propagation =====

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

    public void ApplySystemPromptToAll(string prompt)
    {
        _promptPresets = AppSettings.LoadPromptPresets();
        var active = GetActivePromptPreset();

        foreach (var tab in Tabs)
        {
            tab.ApplySystemPrompt(prompt, _promptPresets, active);
        }
    }

    public void RefreshPromptPresetsOnAll()
    {
        _promptPresets = AppSettings.LoadPromptPresets();
        var active = GetActivePromptPreset();

        foreach (var tab in Tabs)
        {
            tab.ApplyPromptPresets(_promptPresets, active);
        }
    }

    public void RefreshPresetsOnAll()
    {
        var presets = GetPresets();
        foreach (var tab in Tabs)
        {
            tab.ApplyPresets(presets);
        }
    }

    // ===== Helpers =====

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

    private static ChatTheme GetCurrentTheme()
    {
        return AppSettings.Theme switch
        {
            "Light" => ChatTheme.Light,
            "Dark" => ChatTheme.Dark,
            _ => ChatTheme.System,
        };
    }

    private PromptPreset? GetActivePromptPreset()
    {
        var name = AppSettings.ActivePromptPreset;
        return _promptPresets.Find(p => p.Name == name) ?? _promptPresets.FirstOrDefault();
    }

    private string GetActivePromptText()
    {
        return GetActivePromptPreset()?.Text ?? AppSettings.SystemPrompt;
    }
}
