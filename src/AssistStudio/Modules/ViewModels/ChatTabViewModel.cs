using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using AssistStudio.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using Microsoft.UI.Xaml.Controls;
using System.Collections;

namespace AssistStudio.Modules.ViewModels;

/// <summary>
/// View model for a single conversation tab, managing provider state,
/// observable properties for ChatPanel binding, dirty state, and file association.
/// </summary>
public partial class ChatTabViewModel : ObservableObject, IDisposable
{
    #region Observable Fields — Tab

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

    /// <summary>
    /// Built-in server configurations for this conversation.
    /// Null means use AppSettings defaults.
    /// </summary>
    private Dictionary<string, BuiltInServerConfig>? _builtInServers;

    /// <summary>
    /// Unique identifier for this tab, used to differentiate per-tab MCP connections.
    /// </summary>
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// Per-tab Filesystem MCP server connection.
    /// Each tab owns its own server instance with independent folder access.
    /// Null if the profile/conversation has no workspace folders configured.
    /// </summary>
    private McpServerConnection? _filesystemConnection;

    #endregion

    #region Observable Fields — ChatPanel bindings

    /// <summary>
    /// The AI provider for this conversation.
    /// </summary>
    [ObservableProperty] private IAiProvider? _provider;

    /// <summary>
    /// Optional app tasks provider for title generation and summarization.
    /// </summary>
    [ObservableProperty] private IAiProvider? _appTasksProvider;

    /// <summary>
    /// The system prompt sent with AI requests.
    /// </summary>
    [ObservableProperty] private string _systemPrompt = string.Empty;

    /// <summary>
    /// The visual theme for the chat panel.
    /// </summary>
    [ObservableProperty] private ChatTheme _theme;

    /// <summary>
    /// Available provider presets shown in the InputContainer ComboBox.
    /// </summary>
    [ObservableProperty] private IList? _availablePresets;

    /// <summary>
    /// The currently selected provider preset.
    /// </summary>
    [ObservableProperty] private ProviderPreset? _selectedPreset;

    /// <summary>
    /// Available profiles with system prompts.
    /// </summary>
    [ObservableProperty] private IList<Profile>? _availableProfiles;

    /// <summary>
    /// The currently selected profile.
    /// </summary>
    [ObservableProperty] private Profile? _selectedProfile;

    /// <summary>
    /// Whether automatic title generation is enabled.
    /// </summary>
    [ObservableProperty] private bool _autoTitle;

    /// <summary>
    /// Whether automatic conversation summarization is enabled.
    /// </summary>
    [ObservableProperty] private bool _autoSummarize;

    /// <summary>
    /// Whether debug mode is enabled (shows debug UI in WebView2).
    /// </summary>
    [ObservableProperty] private bool _isDebugMode;

    /// <summary>
    /// Tools registered for the current profile (sent in the API tools array).
    /// </summary>
    [ObservableProperty] private IReadOnlyList<IAssistTool> _registeredTools = [];

    /// <summary>
    /// MCP tools that are executable but discovered via <c>search_tools</c> rather than sent in the API tools array.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<IAssistTool> _mcpTools = [];

    /// <summary>
    /// Available servers for the tool flyout. Built from McpRegistry connections.
    /// </summary>
    [ObservableProperty] private IReadOnlyList<ServerInfo> _availableServers = [];

    /// <summary>
    /// Currently enabled server IDs (from profile + runtime toggles).
    /// </summary>
    [ObservableProperty] private IReadOnlyList<string> _enabledServerIds = [];

    #endregion

    #region Properties

    /// <summary>
    /// Gets the icon source for the tab, showing a dot indicator when dirty.
    /// </summary>
    public IconSource? TabIconSource => IsDirty
        ? new FontIconSource
        {
            Glyph = "\uE915",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"),
            Foreground = ThemeHelper.GetBrush("StatusAccentForegroundBrush"),
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

    /// <summary>
    /// Relayed from ChatPanel for keyboard shortcuts forwarded from WebView2.
    /// </summary>
    public event EventHandler<string>? KeyboardShortcutPressed;

    #endregion

    #region Fields

    /// <summary>
    /// Reference to the ChatPanel, set by <see cref="AttachPanel"/> after the View is created.
    /// </summary>
    internal ChatPanel? Panel { get; private set; }

    /// <summary>
    /// When <c>true</c>, <see cref="AttachPanel"/> will focus the input on attach.
    /// Set by <see cref="MainViewModel"/> when a tab is selected before its panel is ready.
    /// </summary>
    internal bool FocusPendingOnAttach { get; set; }

    /// <summary>
    /// Messages queued before the ChatPanel is available (during conversation loading).
    /// </summary>
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId)> _pendingMessages = [];

    /// <summary>
    /// Branch-only messages queued before the ChatPanel is available.
    /// These are registered in the tree but not added to the active path.
    /// </summary>
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId)> _pendingBranchMessages = [];

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
        List<Profile> profiles,
        Profile? selectedProfile,
        int tabNumber = 0)
    {
        CurrentPreset = preset;
        _title = preset.Name;

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var prefix = loader.GetString("Tab_NewConversation");
        _tabHeader = tabNumber > 0 ? $"{prefix} {tabNumber}" : prefix;

        // Set observable fields — ChatTabView.xaml binds to these via x:Bind
        _provider = ProviderFactory.Create(preset);
        _appTasksProvider = ResolveAppTasksProvider(availablePresets);
        _systemPrompt = systemPrompt;
        _theme = theme;
        _availablePresets = availablePresets;
        _selectedPreset = preset;
        _availableProfiles = profiles;
        _selectedProfile = selectedProfile;
        _autoTitle = AppSettings.AppAutoTitle;
        _autoSummarize = AppSettings.AppAutoSummary;
#if DEBUG
        _isDebugMode = true;
#endif

        // Apply linked tools from active profile (built-in + MCP)
        ResolveTools(selectedProfile);
    }

    #endregion

    #region Panel Attachment

    /// <summary>
    /// Called by <c>ChatTabView</c> after the ChatPanel is created and added to the visual tree.
    /// Flushes any pending restored messages and wires the keyboard shortcut relay.
    /// </summary>
    public void AttachPanel(ChatPanel panel)
    {
        Panel = panel;
        LoggingService.LogInfo($"[Tab] Panel attached: {Title}, pending={_pendingMessages.Count}");

        // Initialize workspace folders from per-conversation data (folders belong to conversation, not profile)
        if (_builtInServers is not null
            && _builtInServers.TryGetValue(BuiltInServerHelper.FilesystemKey, out var savedConfig)
            && savedConfig.Folders.Count > 0)
        {
            panel.WorkspaceFolders = savedConfig.Folders;
        }

        // Set workspace capability based on profile
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        panel.IsWorkspaceEnabled = SelectedProfile?.EnabledServers.Contains(filesystemServerId) ?? false;

        // Relay keyboard shortcut from WebView2 (separate HWND)
        panel.KeyboardShortcutPressed += (s, e) => KeyboardShortcutPressed?.Invoke(s, e);

        // Flush branch messages first (tree-only, not active path)
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId) in _pendingBranchMessages)
        {
            var msg = id is not null
                ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = modelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId }
                : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = modelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId };
            panel.RegisterBranchMessage(msg);
        }
        _pendingBranchMessages.Clear();

        // Flush active path messages
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId) in _pendingMessages)
        {
            panel.AddRestoredMessage(role, content, providerName, modelId, id, parentId, toolCalls, toolCallId);
        }
        var hadPending = _pendingMessages.Count > 0;
        _pendingMessages.Clear();

        // If OnLoaded already ran (WebView2 initialized) but _messages was empty at that time,
        // we need to render the messages that were just flushed.
        if (hadPending)
            _ = panel.RenderRestoredMessagesAsync();

        // Force-push tools after template is applied (DispatcherQueue ensures _inputArea is ready)
        if (RegisteredTools.Count > 0)
        {
            var tools = RegisteredTools;
            var mcp = McpTools;
            panel.DispatcherQueue?.TryEnqueue(() =>
            {
                panel.RegisteredTools = [];
                panel.RegisteredTools = tools;
                panel.McpTools = mcp;
            });
        }

        if (FocusPendingOnAttach)
        {
            FocusPendingOnAttach = false;
            panel.DispatcherQueue?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                () => panel.FocusInput());
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Adds a restored message to the chat panel during conversation loading.
    /// If the panel is not yet attached, messages are queued.
    /// </summary>
    public void AddRestoredMessage(ChatRole role, string content, string? providerName, string? providerModelId,
        string? id = null, string? parentId = null,
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null)
    {
        if (Panel is not null)
            Panel.AddRestoredMessage(role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId);
        else
            _pendingMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId));
    }

    /// <summary>
    /// Registers a message in the conversation tree without adding to the active path.
    /// Used for loading inactive branch messages from saved conversations.
    /// </summary>
    public void RegisterBranchMessage(ChatRole role, string content, string? providerName, string? providerModelId,
        string? id = null, string? parentId = null,
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null)
    {
        if (Panel is not null)
        {
            var msg = id is not null
                ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId }
                : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId };
            Panel.RegisterBranchMessage(msg);
        }
        else
        {
            _pendingBranchMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId));
        }
    }

    /// <summary>
    /// Gets the list of chat messages in this conversation.
    /// </summary>
    /// <returns>A read-only list of chat messages.</returns>
    public IReadOnlyList<ChatMessage> GetMessages()
    {
        return Panel?.GetMessages() ?? [];
    }

    /// <summary>
    /// Gets all messages in the conversation tree (active path + all branches).
    /// Used for saving the full tree to disk.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetAllMessages()
    {
        return Panel?.GetAllMessages() ?? [];
    }

    /// <summary>
    /// Gets the ID of the first message on the active path (root-level active child).
    /// Used to restore the correct branch when the first message has been edited.
    /// </summary>
    public string? GetActiveRootChildId()
    {
        return Panel?.GetMessages().FirstOrDefault()?.Id;
    }

    /// <summary>
    /// Applies a visual theme to the chat panel.
    /// </summary>
    public void ApplyTheme(ChatTheme theme) => Theme = theme;

    /// <summary>
    /// Updates the system prompt and profiles on the chat panel.
    /// </summary>
    public void ApplySystemPrompt(string prompt, List<Profile> profiles, Profile? selectedProfile)
    {
        SystemPrompt = prompt;
        AvailableProfiles = profiles;

        // Don't override profile on tabs that already have conversation history
        if (GetMessages().Count == 0)
            SelectedProfile = selectedProfile;
    }

    /// <summary>
    /// Updates the available profiles and selected profile on the chat panel.
    /// Tabs with existing conversation history keep their current profile selection.
    /// </summary>
    public void ApplyProfiles(List<Profile> profiles, Profile? selectedProfile)
    {
        AvailableProfiles = profiles;

        if (GetMessages().Count == 0)
        {
            // Empty tab: switch to the active profile
            SelectedProfile = selectedProfile;
        }
        else
        {
            // Tab with history: keep current profile but sync tool settings from saved data
            var current = Panel?.SelectedProfile;
            if (current is not null)
            {
                var updated = profiles.FirstOrDefault(p => p.Name == current.Name);
                if (updated is not null)
                {
                    current.ToolNames = updated.ToolNames;
                    current.UseSearchTools = updated.UseSearchTools;
                    ResolveTools(current);
                    Panel?.DispatcherQueue.TryEnqueue(() =>
                        {
                            Panel.RegisteredTools = [];
                            Panel.RegisteredTools = RegisteredTools;
                            Panel.McpTools = McpTools;
                        });
                }
            }
        }
    }

    /// <summary>
    /// Updates the available provider presets on the chat panel, preserving the current selection.
    /// </summary>
    public void ApplyPresets(IList presets)
    {
        var currentName = CurrentPreset?.Name;
        var currentModelId = CurrentPreset?.ModelId;
        var currentApiKey = CurrentPreset?.ApiKey;
        var currentBaseUrl = CurrentPreset?.BaseUrl;

        // Force DP change callback by passing a new list instance
        AvailablePresets = new ArrayList(presets);

        var found = false;
        if (currentName is not null)
        {
            foreach (ProviderPreset p in presets)
            {
                if (p.Name == currentName)
                {
                    found = true;
                    SelectedPreset = p;

                    // Recreate provider if connection-relevant fields changed
                    if (p.ModelId != currentModelId ||
                        p.ApiKey != currentApiKey ||
                        p.BaseUrl != currentBaseUrl)
                    {
                        OnPresetChanged(this, p);
                    }
                    break;
                }
            }
        }

        // Fallback: current preset was deleted — select the first available or clear
        if (!found)
        {
            if (presets.Count > 0)
            {
                var first = (ProviderPreset)presets[0]!;
                SelectedPreset = first;
                OnPresetChanged(this, first);
            }
            else
            {
                CurrentPreset = null;
                SelectedPreset = null;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // Disconnect per-tab filesystem server
        if (_filesystemConnection is not null)
        {
            _ = App.McpRegistry.RemoveAsync(_filesystemConnection);
            _filesystemConnection = null;
        }

        if (AppTasksProvider is IDisposable utilDisposable)
        {
            utilDisposable.Dispose();
        }
        if (Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    #endregion

    #region Event Handlers (called by ChatTabView)

    /// <summary>
    /// Handles provider preset changes by disposing the old provider and creating a new one.
    /// </summary>
    public void OnPresetChanged(object? _sender, ProviderPreset preset)
    {
        LoggingService.LogInfo($"[Tab] Preset switched: {preset.Name} (model={preset.ModelId})");

        // Dispose old provider
        if (Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Create new provider — conversation history is preserved
        Provider = ProviderFactory.Create(preset);
        CurrentPreset = preset;
        Title = preset.Name;

        PresetSwitched?.Invoke(this, preset);
    }

    /// <summary>
    /// Handles profile changes by updating the system prompt, workspace folders, and registered tools.
    /// </summary>
    public async void OnProfileChanged(object? _sender, Profile profile)
    {
        LoggingService.LogInfo($"[Tab] Profile changed: {profile.Name}");
        SystemPrompt = profile.Text;

        // Update workspace capability: profile decides if filesystem is enabled
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var isWorkspaceEnabled = profile.EnabledServers.Contains(filesystemServerId);

        if (Panel is not null)
        {
            Panel.IsWorkspaceEnabled = isWorkspaceEnabled;

            if (!isWorkspaceEnabled)
            {
                // Workspace disabled: disconnect server but preserve folder data
                if (_filesystemConnection is not null)
                {
                    await App.McpRegistry.RemoveAsync(_filesystemConnection);
                    _filesystemConnection = null;
                }
            }
            else
            {
                // Workspace enabled: reconnect if conversation has folders
                var folders = Panel.WorkspaceFolders;
                if (folders is { Count: > 0 })
                    await ConnectFilesystemAsync(folders);
            }
        }

        // Resolve tools from both built-in and MCP sources
        ResolveTools(profile);
    }

    /// <summary>
    /// Re-resolves tools for the current profile. Call after profile tool settings
    /// are modified externally (e.g., in ProfilesPage).
    /// Reloads saved profiles to sync changes made in a different Profile instance.
    /// </summary>
    public void RefreshTools()
    {
        var profile = Panel?.SelectedProfile;
        if (profile is null)
        {
            LoggingService.LogInfo("[Settings] RefreshTools: profile is null, skipping");
            return;
        }

        // ProfilesPage uses its own Profile instances, so reload from settings
        // and sync tool-related properties back to the active instance.
        var saved = AppSettings.LoadProfiles();
        var match = saved.FirstOrDefault(p => p.Name == profile.Name);
        if (match is not null)
        {
            profile.ToolNames = match.ToolNames;
            profile.UseSearchTools = match.UseSearchTools;
        }

        LoggingService.LogInfo($"[Settings] RefreshTools: profile={profile.Name}, UseSearchTools={profile.UseSearchTools}, ToolNames={profile.ToolNames.Count}");

        ResolveTools(profile);

        LoggingService.LogInfo($"[Settings] RefreshTools resolved: {RegisteredTools.Count} tools — [{string.Join(", ", RegisteredTools.Select(t => t.Name))}]");

        // Force push to ChatPanel on UI thread
        Panel?.DispatcherQueue.TryEnqueue(() =>
            {
                Panel.RegisteredTools = [];
                Panel.RegisteredTools = RegisteredTools;
                Panel.McpTools = McpTools;
                LoggingService.LogInfo("[Settings] RefreshTools pushed to ChatPanel");
            });
    }

    /// <summary>
    /// Handles the auto-generated title from the utility AI and applies it to the tab.
    /// </summary>
    public void OnTitleGenerated(object? _sender, string title)
    {
        LoggingService.LogInfo($"[Tab] Title generated: {title}");
        Title = title;
        IsDirty = true;
    }

    /// <summary>
    /// Handles the user requesting to edit the conversation title via a rename dialog.
    /// </summary>
    public async void OnTitleEditRequested(object? _sender, string currentTitle)
    {
        if (Panel is null) return;

        var input = new TextBox { Text = currentTitle, SelectionStart = currentTitle.Length };
        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var dialog = new ThemedContentDialog
        {
            Title = loader.GetString("Dialog_RenameConversation"),
            Content = input,
            PrimaryButtonText = loader.GetString("Dialog_OK"),
            CloseButtonText = loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Panel.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(input.Text))
        {
            Title = input.Text.Trim();
            IsDirty = true;
        }
    }

    /// <summary>
    /// Handles server toggle changes from the tool flyout.
    /// Updates the profile's enabled servers and re-resolves tools.
    /// </summary>
    public void OnEnabledServersChanged(object? _sender, IReadOnlyList<string> serverIds)
    {
        var profile = Panel?.SelectedProfile;
        if (profile is not null)
        {
            profile.EnabledServers = [.. serverIds];
            AppSettings.SaveCustomProfiles(AppSettings.LoadProfiles()
                .Select(p => p.Name == profile.Name ? profile : p));
        }

        ResolveTools(profile);
    }

    /// <summary>
    /// Handles workspace folder changes from the title bar flyout.
    /// If the per-tab Filesystem server is already connected, updates folders via
    /// roots protocol (no process restart). Otherwise connects a new server instance.
    /// </summary>
    public async void OnWorkspaceFoldersChanged(object? _sender, IReadOnlyList<string> folders)
    {
        // Save folder data to conversation (folders belong to conversation, not profile)
        var config = new BuiltInServerConfig
        {
            IsEnabled = folders.Count > 0,
            Folders = [.. folders],
        };
        _builtInServers = folders.Count > 0
            ? new Dictionary<string, BuiltInServerConfig> { [BuiltInServerHelper.FilesystemKey] = config }
            : null;
        IsDirty = true;

        // Only connect/update server if profile has Workspace enabled
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var isWorkspaceEnabled = Panel?.SelectedProfile?.EnabledServers.Contains(filesystemServerId) ?? false;

        if (isWorkspaceEnabled)
        {
            // Already connected → roots notification only (no process restart)
            if (_filesystemConnection is not null && _filesystemConnection.IsConnected && folders.Count > 0)
            {
                await _filesystemConnection.UpdateWorkspaceFoldersAsync(folders);
            }
            else
            {
                // First connection or all folders removed
                await ConnectFilesystemAsync(folders);
            }
        }

        ResolveTools(Panel?.SelectedProfile);
    }

    /// <summary>
    /// Marks the conversation as dirty when a new message is added.
    /// </summary>
    public void OnMessageAdded(object? _sender, ChatMessage _message)
    {
        IsDirty = true;
    }

    /// <summary>
    /// Marks the conversation as dirty when the user switches branches.
    /// </summary>
    public void OnBranchChanged(object? _sender, EventArgs _e)
    {
        IsDirty = true;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Connects (or reconnects) the per-tab Filesystem MCP server with the given folders.
    /// If already connected, disconnects first. If no folders, just disconnects.
    /// </summary>
    private async Task ConnectFilesystemAsync(IReadOnlyList<string> folders)
    {
        // Disconnect existing per-tab filesystem server
        if (_filesystemConnection is not null)
        {
            await App.McpRegistry.RemoveAsync(_filesystemConnection);
            _filesystemConnection = null;
        }

        if (folders.Count == 0) return;

        var mcpConfig = BuiltInServerHelper.CreateMcpServerConfig(
            BuiltInServerHelper.FilesystemKey,
            new BuiltInServerConfig { IsEnabled = true, Folders = [.. folders] });
        if (mcpConfig is null) return;

        // Unique ID per tab to allow multiple filesystem instances in the registry
        mcpConfig.Id = $"builtin_{BuiltInServerHelper.FilesystemKey}_{Id}";

        _filesystemConnection = await App.McpRegistry.AddAndConnectAsync(
            mcpConfig, supportsRoots: true);
    }

    /// <summary>
    /// Resolves registered tools using server-level selection.
    /// Built-in tools (run_command, fetch_url) are always included.
    /// File tools (read_file, write_file, search_files) are included only when Filesystem MCP is inactive.
    /// All MCP tools are discovered via search_tools meta-tool, not registered individually.
    /// </summary>
    private void ResolveTools(Profile? profile)
    {
        var tools = new List<IAssistTool>();
        var profileToolNames = profile?.ToolNames ?? [];

        // 1. Built-in tools filtered by profile selection
        var filesystemConn = _filesystemConnection;
        var filesystemActiveInProfile = filesystemConn?.IsConnected == true
            && (profile?.EnabledServers.Contains($"builtin_{BuiltInServerHelper.FilesystemKey}") ?? false);

        foreach (var toolName in new[] { "run_command", "fetch_url", "read_file", "write_file", "search_files" })
        {
            if (!profileToolNames.Contains(toolName)) continue;

            // File tools suppressed when Filesystem MCP is active
            if (filesystemActiveInProfile && BuiltInServerHelper.SuppressedBuiltInToolNames.Contains(toolName))
                continue;

            tools.AddRange(ToolRegistry.Resolve([toolName]));
        }

        // 2. search_tools — only when profile has enabled servers
        var enabledServerIds = profile?.EnabledServers ?? [];

        // Map "builtin_filesystem" to the per-tab connection ID so filtering works
        var effectiveServerIds = enabledServerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_filesystemConnection is not null
            && effectiveServerIds.Contains($"builtin_{BuiltInServerHelper.FilesystemKey}"))
        {
            effectiveServerIds.Add(_filesystemConnection.Config.Id);
        }

        if (effectiveServerIds.Count > 0)
        {
            var searchTool = ToolRegistry.Resolve(["search_tools"]).FirstOrDefault();
            if (searchTool is not null)
            {
                if (searchTool is ISearchToolScope scoped)
                {
                    // Scope search to tools from enabled servers only
                    var allowedToolNames = App.McpRegistry.AllTools
                        .Where(t => effectiveServerIds.Contains(
                            App.McpRegistry.Connections
                                .FirstOrDefault(c => c.Tools.Contains(t))?.Config.Id ?? ""))
                        .Select(t => t.Name)
                        .ToHashSet();

                    scoped.AllowedToolNames = allowedToolNames.Count > 0 ? allowedToolNames : null;
                }

                tools.Add(searchTool);
            }
        }

        RegisteredTools = tools;

        // Collect MCP tools for execution (not sent in API tools array, discovered via search_tools)
        if (effectiveServerIds.Count > 0)
        {
            McpTools = [.. App.McpRegistry.AllTools
                .Where(t => effectiveServerIds.Contains(
                    App.McpRegistry.Connections
                        .FirstOrDefault(c => c.Tools.Contains(t))?.Config.Id ?? ""))];
        }
        else
        {
            McpTools = [];
        }

        // Update server list for the tool flyout UI (filtered by profile's enabled servers)
        AvailableServers = [.. App.McpRegistry.Connections
            .Where(c => effectiveServerIds.Contains(c.Config.Id))
            .Select(c => new ServerInfo(
                c.Config.Id, c.Config.Name, c.IsConnected, c.Config.IsBuiltIn))];

        EnabledServerIds = enabledServerIds;
    }

    /// <summary>
    /// Resolves the app tasks provider based on the user's settings, returning <c>null</c>
    /// if the source is not set to "Specific" or no matching preset is found.
    /// </summary>
    /// <returns>The app tasks provider, or <c>null</c> if not configured.</returns>
    private static IAiProvider? ResolveAppTasksProvider(IList availablePresets)
    {
        if (AppSettings.AppTasksSource != "Specific") return null;

        var presetName = AppSettings.AppTasksPreset;
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
