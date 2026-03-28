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

    /// <summary>
    /// Per-tab Knowledge Archive MCP server connection.
    /// Each tab owns its own RAG server instance with a single archive folder.
    /// </summary>
    private McpServerConnection? _ragConnection;

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
    /// Available provider presets shown in the ComposeBar ComboBox.
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
    /// Raised when the user switches provider preset via ComposeBar ComboBox.
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
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId, string? ActiveChildId)> _pendingMessages = [];

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

        // Subscribe to profile tool settings changes (shared instance)
        if (selectedProfile is not null)
            selectedProfile.ToolSettingsChanged += OnProfileToolSettingsChanged;
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

        // Initialize Knowledge Archive folder from per-conversation data (skip if folder deleted)
        if (_builtInServers is not null
            && _builtInServers.TryGetValue(BuiltInServerHelper.RagKey, out var ragSavedConfig)
            && ragSavedConfig.Folders.Count > 0
            && Directory.Exists(ragSavedConfig.Folders[0]))
        {
            panel.KnowledgeArchiveFolder = ragSavedConfig.Folders[0];
        }

        // Set workspace capability based on profile
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        panel.IsWorkspaceEnabled = SelectedProfile?.EnabledServers.Contains(filesystemServerId) ?? false;

        // Set Knowledge Archive capability based on profile
        var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
        panel.IsKnowledgeArchiveEnabled = SelectedProfile?.EnabledServers.Contains(ragServerId) ?? false;

        // Reconnect RAG server if the conversation had a Knowledge Archive folder and
        // the active profile enables RAG. Without this, save→load loses the search_documents tool.
        if (!string.IsNullOrEmpty(panel.KnowledgeArchiveFolder)
            && (SelectedProfile?.EnabledServers.Contains(ragServerId) ?? false))
        {
            _ = ConnectRagAsync(panel.KnowledgeArchiveFolder);
        }

        // Relay keyboard shortcut from WebView2 (separate HWND)
        panel.KeyboardShortcutPressed += (s, e) => KeyboardShortcutPressed?.Invoke(s, e);

        // Wire send-time tool resolution (auto-connect + connection filtering)
        panel.PrepareToolsForSendAsync = PrepareToolsForSendAsync;

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
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId) in _pendingMessages)
        {
            panel.AddRestoredMessage(role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId);
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
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null,
        string? activeChildId = null)
    {
        if (Panel is not null)
            Panel.AddRestoredMessage(role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId);
        else
            _pendingMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId));
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
    /// Gets the built-in server configurations for this conversation (workspace folders, archive folder).
    /// Used when saving the conversation to .astd file.
    /// </summary>
    public Dictionary<string, BuiltInServerConfig>? GetBuiltInServers() => _builtInServers;

    /// <summary>
    /// Sets the built-in server configurations (used when loading from .astd file).
    /// </summary>
    public void SetBuiltInServers(Dictionary<string, BuiltInServerConfig>? servers) => _builtInServers = servers;

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
    /// Updates the available profiles and selected profile on the chat panel.
    /// Tabs with existing conversation history keep their current profile selection.
    /// </summary>
    public void ApplyProfiles(List<Profile> profiles, Profile? _selectedProfile)
    {
        // Update available list only — never change the tab's selected profile.
        // Profile is set once at tab creation; user changes it via ComposeBar ComboBox.
        AvailableProfiles = profiles;
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

    /// <summary>
    /// Handles tool settings changes from the shared Profile instance.
    /// </summary>
    private void OnProfileToolSettingsChanged(object? sender, EventArgs e)
    {
        if (sender is not Profile profile) return;
        LoggingService.LogInfo($"[Tab] Profile.ToolSettingsChanged: {profile.Name}");
        RefreshTools();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // Unsubscribe from profile events
        if (SelectedProfile is not null)
            SelectedProfile.ToolSettingsChanged -= OnProfileToolSettingsChanged;

        // Disconnect per-tab MCP servers
        if (_filesystemConnection is not null)
        {
            _ = App.McpRegistry.RemoveAsync(_filesystemConnection);
            _filesystemConnection = null;
        }
        if (_ragConnection is not null)
        {
            _ = App.McpRegistry.RemoveAsync(_ragConnection);
            _ragConnection = null;
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

        // Unsubscribe from old profile, subscribe to new
        if (SelectedProfile is not null)
            SelectedProfile.ToolSettingsChanged -= OnProfileToolSettingsChanged;
        profile.ToolSettingsChanged += OnProfileToolSettingsChanged;

        SystemPrompt = profile.SystemPrompt;

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

            // Knowledge Archive capability
            var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
            var isRagEnabled = profile.EnabledServers.Contains(ragServerId);
            Panel.IsKnowledgeArchiveEnabled = isRagEnabled;

            if (!isRagEnabled)
            {
                await DisconnectRagAsync();
            }
            else
            {
                var archiveFolder = Panel.KnowledgeArchiveFolder;
                if (!string.IsNullOrEmpty(archiveFolder))
                    await ConnectRagAsync(archiveFolder);
            }
        }

        // Resolve tools from both built-in and MCP sources
        ResolveTools(profile);
    }

    /// <summary>
    /// Re-resolves tools for the current profile. Called when profile tool settings
    /// change (shared instances via AppSettings cache, no disk reload needed).
    /// </summary>
    public void RefreshTools()
    {
        var profile = Panel?.SelectedProfile;
        if (profile is null)
        {
            LoggingService.LogInfo("[Settings] RefreshTools: profile is null, skipping");
            return;
        }

        LoggingService.LogInfo($"[Settings] RefreshTools: profile={profile.Name}, UseSearchTools={profile.UseSearchTools}, ToolNames={profile.ToolNames.Count}");

        // Sync system prompt from profile
        SystemPrompt = profile.SystemPrompt;

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
    /// Handles Knowledge Archive folder changes from the title bar flyout.
    /// Connects the per-tab RAG server and starts indexing.
    /// </summary>
    public async void OnKnowledgeArchiveFolderChanged(object? _sender, string? folder)
    {
        // Save to conversation data
        if (_builtInServers is null)
            _builtInServers = new Dictionary<string, BuiltInServerConfig>();

        if (!string.IsNullOrEmpty(folder))
        {
            _builtInServers[BuiltInServerHelper.RagKey] = new BuiltInServerConfig
            {
                IsEnabled = true,
                Folders = [folder],
                EnvironmentVariableKeys =
                [
                    "EMBEDDING_BASE_URL", "EMBEDDING_API_KEY",
                    "EMBEDDING_MODEL", "EMBEDDING_DIMENSION",
                    "CONTEXTUALIZER_PROVIDER", "CONTEXTUALIZER_BASE_URL",
                    "CONTEXTUALIZER_API_KEY", "CONTEXTUALIZER_MODEL",
                ],
            };
        }
        else
        {
            _builtInServers.Remove(BuiltInServerHelper.RagKey);
        }
        IsDirty = true;

        // Connect/disconnect RAG server if profile has it enabled
        var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
        var isRagEnabled = Panel?.SelectedProfile?.EnabledServers.Contains(ragServerId) ?? false;

        if (isRagEnabled && !string.IsNullOrEmpty(folder))
        {
            await ConnectRagAsync(folder);
        }
        else
        {
            await DisconnectRagAsync();
        }

        ResolveTools(Panel?.SelectedProfile);
    }

    /// <summary>
    /// Handles re-index request from the Knowledge Archive flyout.
    /// Runs index_documents with force=true in background.
    /// </summary>
    public async void OnKnowledgeArchiveReindexRequested(object? _sender, EventArgs _e)
    {
        if (_ragConnection is null || !_ragConnection.IsConnected)
        {
            LoggingService.LogWarning("[RAG] Re-index requested but RAG not connected");
            return;
        }

        await RunIndexDocumentsAsync(force: true, label: "Re-indexing");
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
    /// Connects (or reconnects) the per-tab RAG MCP server for a single archive folder.
    /// Syncs embedding env vars from AppSettings, then starts indexing with notification.
    /// </summary>
    private async Task ConnectRagAsync(string folder)
    {
        LoggingService.LogInfo($"[RAG] Connecting to folder: {folder}");

        // Skip if folder no longer exists on disk
        if (!Directory.Exists(folder))
        {
            LoggingService.LogWarning($"[RAG] Knowledge Archive folder not found: {folder}");
            return;
        }

        await DisconnectRagAsync();

        // Sync embedding + contextualizer env vars to vault
        const string ragId = "builtin_rag";
        PasswordVaultHelper.SaveMcpEnvVar(ragId, "EMBEDDING_BASE_URL", AppSettings.EmbeddingBaseUrl);
        PasswordVaultHelper.SaveMcpEnvVar(ragId, "EMBEDDING_MODEL", AppSettings.EmbeddingModel);
        PasswordVaultHelper.SaveMcpEnvVar(ragId, "EMBEDDING_DIMENSION", "0");
        PasswordVaultHelper.SaveMcpEnvVar(ragId, "CONTEXTUALIZER_PROVIDER", AppSettings.ContextualizerProvider);
        PasswordVaultHelper.SaveMcpEnvVar(ragId, "CONTEXTUALIZER_BASE_URL", AppSettings.ContextualizerBaseUrl);
        PasswordVaultHelper.SaveMcpEnvVar(ragId, "CONTEXTUALIZER_MODEL", AppSettings.ContextualizerModel);

        var ragConfig = new BuiltInServerConfig
        {
            IsEnabled = true,
            Folders = [folder],
            EnvironmentVariableKeys =
            [
                "EMBEDDING_BASE_URL", "EMBEDDING_API_KEY",
                "EMBEDDING_MODEL", "EMBEDDING_DIMENSION",
                "CONTEXTUALIZER_PROVIDER", "CONTEXTUALIZER_BASE_URL",
                "CONTEXTUALIZER_API_KEY", "CONTEXTUALIZER_MODEL",
            ],
        };

        var mcpConfig = BuiltInServerHelper.CreateMcpServerConfig(BuiltInServerHelper.RagKey, ragConfig);
        if (mcpConfig is null) return;

        mcpConfig.Id = $"builtin_{BuiltInServerHelper.RagKey}_{Id}";

        _ragConnection = await App.McpRegistry.AddAndConnectAsync(mcpConfig);
        LoggingService.LogInfo($"[RAG] Connection result: IsConnected={_ragConnection.IsConnected}, Tools={_ragConnection.Tools.Count}");

        // Start indexing in background with notification
        if (_ragConnection.IsConnected)
        {
            _ = IndexKnowledgeArchiveAsync();
        }
    }

    /// <summary>
    /// Disconnects the per-tab RAG server if connected.
    /// </summary>
    private async Task DisconnectRagAsync()
    {
        if (_ragConnection is not null)
        {
            await App.McpRegistry.RemoveAsync(_ragConnection);
            _ragConnection = null;
        }
    }

    /// <summary>
    /// Runs index_documents on the connected RAG server with progress tracking.
    /// </summary>
    private Task IndexKnowledgeArchiveAsync() =>
        RunIndexDocumentsAsync(force: false, label: "Indexing");

    /// <summary>
    /// Shared indexing logic used by both initial connect and manual re-index.
    /// Tracks progress via MCP notifications and updates ChatPanel UI.
    /// </summary>
    private async Task RunIndexDocumentsAsync(bool force, string label)
    {
        if (_ragConnection is null || !_ragConnection.IsConnected) return;

        var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
        var title = loader.GetString("Connect_KnowledgeArchiveTitle") ?? "Knowledge Archive";
        var progressMsg = force
            ? loader.GetString("Connect_KnowledgeArchiveReindexing") ?? "Re-indexing documents…"
            : loader.GetString("Connect_KnowledgeArchiveIndexing") ?? "Indexing documents…";
        var readyTitle = loader.GetString("Connect_KnowledgeArchiveReady") ?? "Knowledge Archive — Ready";
        var failedTitle = loader.GetString("Connect_KnowledgeArchiveFailed") ?? "Knowledge Archive — Failed";
        var embeddingHint = loader.GetString("Connect_EmbeddingModelHint")
            ?? "Make sure an embedding model is loaded: ollama pull nomic-embed-text";

        LoggingService.LogInfo($"[RAG] {label} started (force={force})");
        NotificationCenter.Instance.Post(
            Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational,
            title,
            progressMsg);

        // Set indexing state on ChatPanel
        if (Panel is not null)
        {
            Panel.DispatcherQueue.TryEnqueue(() =>
            {
                Panel.IsArchiveIndexing = true;
                Panel.ArchiveIndexingProgress = 0;
                Panel.ArchiveIndexingText = "";
                Panel.UpdateArchiveProgressUI();
            });
        }

        try
        {
            var args = System.Text.Json.JsonSerializer.SerializeToElement(new { force });
            LoggingService.LogInfo($"[RAG] Calling index_documents tool (force={force})…");

            var progress = new Progress<(double Current, double Total, string? Message)>(value =>
            {
                var pct = value.Total > 0 ? value.Current / value.Total * 100 : 0;
                LoggingService.LogInfo($"[RAG] Progress: {value.Current}/{value.Total} — {value.Message}");

                if (Panel is not null)
                {
                    Panel.DispatcherQueue.TryEnqueue(() =>
                    {
                        Panel.ArchiveIndexingProgress = pct;
                        Panel.ArchiveIndexingText = value.Message ?? $"{value.Current}/{value.Total}";
                        Panel.UpdateArchiveProgressUI();
                    });
                }
            });

            var resultJson = await _ragConnection.CallToolWithProgressAsync(
                "index_documents", args, progress);
            LoggingService.LogInfo($"[RAG] index_documents result: {resultJson}");

            var message = ParseIndexResult(resultJson, label);

            NotificationCenter.Instance.Post(
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success,
                readyTitle,
                message,
                5000);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[RAG] {label} failed: {ex.Message}");
            NotificationCenter.Instance.Post(
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error,
                failedTitle,
                $"{ex.Message}\n{embeddingHint}",
                8000);
        }
        finally
        {
            if (Panel is not null)
            {
                Panel.DispatcherQueue.TryEnqueue(() =>
                {
                    Panel.IsArchiveIndexing = false;
                    Panel.ArchiveIndexingProgress = 0;
                    Panel.ArchiveIndexingText = "";
                    Panel.UpdateArchiveProgressUI();
                });
            }
        }
    }

    private static string ParseIndexResult(string? resultJson, string label)
    {
        if (string.IsNullOrEmpty(resultJson))
            return $"{label} complete.";

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
            var root = doc.RootElement;
            var indexed = root.TryGetProperty("indexed", out var i) ? i.GetInt32() : 0;
            var skipped = root.TryGetProperty("skipped", out var s) ? s.GetInt32() : 0;
            var chunks = root.TryGetProperty("total_chunks", out var c) ? c.GetInt32() : 0;
            var failed = root.TryGetProperty("failed", out var f) ? f.GetInt32() : 0;
            var removed = root.TryGetProperty("removed", out var r) ? r.GetInt32() : 0;

            var parts = new List<string>();
            if (indexed > 0) parts.Add($"{indexed} indexed");
            if (skipped > 0) parts.Add($"{skipped} unchanged");
            if (removed > 0) parts.Add($"{removed} removed");
            if (failed > 0) parts.Add($"{failed} failed");
            parts.Add($"{chunks} chunks");

            var message = string.Join(", ", parts);
            LoggingService.LogInfo($"[RAG] {label} complete: {message}");
            return message;
        }
        catch
        {
            LoggingService.LogWarning($"[RAG] Could not parse index result: {resultJson}");
            return resultJson!;
        }
    }

    /// <summary>
    /// Resolves registered tools for Flyout display (connection-independent).
    /// Actual connection filtering happens at send time via <see cref="PrepareToolsForSendAsync"/>.
    /// </summary>
    private void ResolveTools(Profile? profile)
    {
        var tools = new List<IAssistTool>();
        var profileToolNames = profile?.ToolNames ?? [];

        // 1. Built-in tools filtered by profile selection
        // Suppress file tools when Filesystem MCP is enabled in the profile (regardless of connection state)
        var filesystemEnabledInProfile =
            profile?.EnabledServers.Contains($"builtin_{BuiltInServerHelper.FilesystemKey}") ?? false;

        foreach (var toolName in new[] { "run_command", "fetch_url", "read_file", "write_file", "search_files" })
        {
            if (!profileToolNames.Contains(toolName)) continue;

            if (filesystemEnabledInProfile && BuiltInServerHelper.SuppressedBuiltInToolNames.Contains(toolName))
                continue;

            tools.AddRange(ToolRegistry.Resolve([toolName]));
        }

        // 2. search_tools — when profile has enabled servers
        var enabledServerIds = profile?.EnabledServers ?? [];
        var effectiveServerIds = BuildEffectiveServerIds(enabledServerIds);

        if (effectiveServerIds.Count > 0)
        {
            var searchTool = ToolRegistry.Resolve(["search_tools"]).FirstOrDefault();
            if (searchTool is not null)
            {
                if (searchTool is ISearchToolScope scoped)
                {
                    // Scope to all enabled servers' tools (connection-independent)
                    var allowedToolNames = GetToolNamesFromServers(effectiveServerIds);
                    scoped.AllowedToolNames = allowedToolNames.Count > 0 ? allowedToolNames : null;
                }

                tools.Add(searchTool);
            }
        }

        // 3. Server placeholders — for flyout display (same logic as ProfilesPage)
        // Built-in servers: always available, check if enabled in profile
        var enabledSet = enabledServerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filesystemId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var ragId = $"builtin_{BuiltInServerHelper.RagKey}";

        if (enabledSet.Contains(filesystemId))
        {
            tools.Add(new ServerPlaceholderTool
            {
                Name = filesystemId,
                DisplayName = BuiltInServerHelper.FilesystemDisplayName,
            });
        }
        if (enabledSet.Contains(ragId))
        {
            tools.Add(new ServerPlaceholderTool
            {
                Name = ragId,
                DisplayName = BuiltInServerHelper.RagDisplayName,
            });
        }

        // External servers: from McpRegistry (same source as ProfilesPage)
        foreach (var conn in App.McpRegistry.Connections.Where(c => !c.Config.IsBuiltIn))
        {
            if (enabledSet.Contains(conn.Config.Id))
            {
                tools.Add(new ServerPlaceholderTool
                {
                    Name = conn.Config.Id,
                    DisplayName = conn.Config.Name,
                });
            }
        }

        RegisteredTools = tools;

        // McpTools are resolved at send time by PrepareToolsForSendAsync
        McpTools = [];

        // Set run_command default CWD to first workspace folder
        var runCmd = ToolRegistry.Resolve(["run_command"]).OfType<RunCommandTool>().FirstOrDefault();
        if (runCmd is not null)
        {
            var folders = Panel?.WorkspaceFolders;
            runCmd.DefaultWorkingDirectory = folders is { Count: > 0 } ? folders[0] : null;
        }
    }

    /// <summary>
    /// Auto-connects disconnected servers and filters tools to only those from connected servers.
    /// Called by ChatPanel before sending the API request.
    /// </summary>
    public async Task<IReadOnlyList<IAssistTool>> PrepareToolsForSendAsync(
        IReadOnlyList<IAssistTool> selectedTools)
    {
        // Separate real tools from server placeholders
        var realTools = new List<IAssistTool>();
        var enabledServerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in selectedTools)
        {
            if (tool is ServerPlaceholderTool placeholder)
                enabledServerIds.Add(placeholder.Name);
            else
                realTools.Add(tool);
        }

        if (enabledServerIds.Count == 0)
            return realTools;

        var effectiveServerIds = BuildEffectiveServerIds(enabledServerIds);

        // 1. Auto-connect disconnected servers
        foreach (var serverId in effectiveServerIds)
        {
            var conn = ResolveConnection(serverId);
            if (conn is not null && !conn.IsConnected)
            {
                try
                {
                    await conn.ConnectAsync();
                    LoggingService.LogInfo($"[Send] Auto-connected server: {serverId}");
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning($"[Send] Failed to auto-connect server {serverId}: {ex.Message}");
                }
            }
        }

        // 2. Determine which servers are now connected
        var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serverId in effectiveServerIds)
        {
            var conn = ResolveConnection(serverId);
            if (conn?.IsConnected == true)
                connectedIds.Add(serverId);
        }

        // 3. Build final tool list from real tools + connected server tools
        var result = new List<IAssistTool>();
        foreach (var tool in realTools)
        {
            if (IsBuiltInTool(tool.Name))
            {
                result.Add(tool);
            }
            else if (tool.Name == "search_tools")
            {
                if (connectedIds.Count > 0)
                {
                    if (tool is ISearchToolScope scoped)
                        scoped.AllowedToolNames = GetToolNamesFromServers(connectedIds);
                    result.Add(tool);
                }
            }
        }

        // 4. Add directly-exposed tools from connected servers (e.g., RAG search_documents)
        if (_ragConnection?.IsConnected == true
            && connectedIds.Contains(_ragConnection.Config.Id))
        {
            foreach (var ragTool in _ragConnection.Tools)
                result.Add(ragTool);
        }

        // 5. Update McpTools for ToolCallExecutor (search_tools-discovered tools)
        McpTools = connectedIds.Count > 0
            ? [.. GetToolsFromServers(connectedIds)]
            : [];

        return result;
    }

    #region Tool Resolution Helpers

    /// <summary>Built-in tool names that don't require MCP server connections.</summary>
    private static readonly HashSet<string> BuiltInToolNames =
        ["run_command", "fetch_url", "read_file", "write_file", "search_files"];

    private static bool IsBuiltInTool(string name) => BuiltInToolNames.Contains(name);

    /// <summary>
    /// Maps profile-level server IDs (builtin_xxx) to per-tab connection IDs.
    /// </summary>
    private HashSet<string> BuildEffectiveServerIds(IEnumerable<string> enabledServerIds)
    {
        var effective = enabledServerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_filesystemConnection is not null
            && effective.Contains($"builtin_{BuiltInServerHelper.FilesystemKey}"))
        {
            effective.Add(_filesystemConnection.Config.Id);
        }
        if (_ragConnection is not null
            && effective.Contains($"builtin_{BuiltInServerHelper.RagKey}"))
        {
            effective.Add(_ragConnection.Config.Id);
        }
        return effective;
    }

    /// <summary>Resolves a server ID to its <see cref="McpServerConnection"/>.</summary>
    private McpServerConnection? ResolveConnection(string serverId)
    {
        if (_filesystemConnection?.Config.Id == serverId) return _filesystemConnection;
        if (_ragConnection?.Config.Id == serverId) return _ragConnection;
        return App.McpRegistry.Connections.FirstOrDefault(c => c.Config.Id == serverId);
    }

    /// <summary>Finds the connection that owns the given tool.</summary>
    private McpServerConnection? FindConnectionForTool(IAssistTool tool)
    {
        if (_ragConnection?.Tools.Contains(tool) == true) return _ragConnection;
        if (_filesystemConnection?.Tools.Contains(tool) == true) return _filesystemConnection;
        return App.McpRegistry.Connections.FirstOrDefault(c => c.Tools.Contains(tool));
    }

    /// <summary>Gets tool names from servers matching the given IDs.</summary>
    private static HashSet<string> GetToolNamesFromServers(HashSet<string> serverIds)
    {
        return [.. App.McpRegistry.AllTools
            .Where(t => serverIds.Contains(
                App.McpRegistry.Connections
                    .FirstOrDefault(c => c.Tools.Contains(t))?.Config.Id ?? ""))
            .Select(t => t.Name)];
    }

    /// <summary>Gets a display name for a server ID.</summary>
    private static string GetServerDisplayName(string serverId)
    {
        // Built-in servers: use known display names
        if (serverId.StartsWith("builtin_"))
        {
            var key = serverId["builtin_".Length..];
            return BuiltInServerHelper.GetDisplayName(key);
        }

        // External servers: look up from McpRegistry connections
        var conn = App.McpRegistry.Connections.FirstOrDefault(c => c.Config.Id == serverId);
        return conn?.Config.Name ?? serverId;
    }

    /// <summary>Gets tool instances from servers matching the given IDs.</summary>
    private static IEnumerable<IAssistTool> GetToolsFromServers(HashSet<string> serverIds)
    {
        return App.McpRegistry.AllTools
            .Where(t => serverIds.Contains(
                App.McpRegistry.Connections
                    .FirstOrDefault(c => c.Tools.Contains(t))?.Config.Id ?? ""));
    }

    #endregion

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
