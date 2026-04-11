using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using AssistStudio.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.Ai.Execution;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Models;
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
    /// Cached sub-agent tool instance. Invalidated on preset change.
    /// </summary>
    private SubAgentTool? _subAgentTool;

    /// <summary>
    /// Parent conversation's enabled server IDs, cached from UI thread for thread-safe access
    /// by <see cref="ResolveToolsForSubAgentAsync"/> which runs on ThreadPool.
    /// Updated in <see cref="ResolveTools"/>.
    /// </summary>
    private IReadOnlyList<string> _parentEnabledServers = [];

    /// <summary>
    /// Cached Knowledge Archive folder (kb_id) from UI thread for thread-safe access
    /// by <see cref="SubAgentTool.BuildContextHints"/> which runs on ThreadPool.
    /// Updated in <see cref="ResolveTools"/>.
    /// </summary>
    private string? _cachedKbId;

    // RAG is now a shared multi-KB server — no per-tab connection needed.
    // The conversation stores a selected KB ID via _builtInServers[RagKey].

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
    /// Stable unique identifier for this conversation, used as the media storage folder key.
    /// </summary>
    public string? ConversationId { get; set; }

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
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId, string? ActiveChildId, IReadOnlyList<ChatAttachment>? Attachments, IReadOnlyList<MediaContent>? ToolMedia, string? ThinkingContent, DateTime? Timestamp, double? ElapsedSeconds, int? TokenCount, SummaryMeta? Summary)> _pendingMessages = [];

    /// <summary>
    /// Branch-only messages queued before the ChatPanel is available.
    /// These are registered in the tree but not added to the active path.
    /// </summary>
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId, string? ActiveChildId, DateTime? Timestamp)> _pendingBranchMessages = [];

    #endregion

    #region Observable Property Changed Handlers

    /// <summary>
    /// Notifies the UI that the tab icon source may have changed when dirty state changes.
    /// Triggers debounced auto-save when the conversation becomes dirty.
    /// </summary>
    partial void OnIsDirtyChanged(bool value)
    {
        OnPropertyChanged(nameof(TabIconSource));

        if (value)
        {
            var messages = GetAllMessages();
            if (messages.Count > 0)
            {
                ConversationId ??= Guid.NewGuid().ToString("N");
                ConversationManager.ScheduleAutoSave(
                    FilePath, Title, CurrentPreset?.Name, messages,
                    GetActiveRootChildId(), GetBuiltInServers(), ConversationId);
            }
        }
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

    // ── WebView2 Lifecycle ──────────────────────────────────────────────
    //
    // WinUI 3 TabView internally recycles XAML containers (ChatTabView + ChatPanel)
    // when tabs are closed and new ones are created. This means a single ChatPanel
    // instance — and its WebView2 — can outlive the ViewModel it was originally
    // bound to, causing "visual ghosting" where stale DOM from a previous
    // conversation appears in a new tab.
    //
    // Current solution (Dispose + Reinitialize):
    //   1. ChatTabViewModel.Dispose() calls ChatPanel.Dispose()
    //      → navigates to about:blank, calls WebView2.Close(),
    //        removes the control from the visual tree, resets layout.
    //   2. When TabView recycles the container for a new ViewModel,
    //      AttachPanel() detects IsInitialized == false and calls
    //      ChatPanel.ReinitializeWebViewAsync()
    //      → creates a brand-new WebView2 instance, inserts it into the
    //        Grid, and runs the full renderer initialization.
    //
    // This "never reuse, always recreate" strategy is simple and effective.
    // If ghosting ever resurfaces (e.g. due to TabView behavior changes),
    // consider escalating to WebView2 Multi-Profile API
    // (CoreWebView2ControllerOptions.ProfileName + IsInPrivateModeEnabled)
    // which provides browser-level session isolation per tab.
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <c>ChatTabView</c> after the ChatPanel is created and added to the visual tree.
    /// Flushes any pending restored messages and wires the keyboard shortcut relay.
    /// </summary>
    public async void AttachPanel(ChatPanel panel)
    {
        Panel = panel;
        LoggingService.LogInfo($"[Tab] Panel attached: {Title}, pending={_pendingMessages.Count}, initialized={panel.IsInitialized}");

        // If the panel was previously disposed (WebView2 closed), create a new WebView2.
        // This happens when TabView recycles a container after the old tab was closed.
        if (!panel.IsInitialized)
        {
            LoggingService.LogInfo("[Tab] Panel needs WebView2 reinitialization (recycled container)");
            await panel.ReinitializeWebViewAsync();
        }

        // Initialize workspace folders from per-conversation data (folders belong to conversation, not profile)
        if (_builtInServers is not null
            && _builtInServers.TryGetValue(BuiltInServerHelper.FilesystemKey, out var savedConfig)
            && savedConfig.Folders.Count > 0)
        {
            panel.WorkspaceFolders = savedConfig.Folders;
        }

        // Initialize Knowledge Base ID from per-conversation data
        if (_builtInServers is not null
            && _builtInServers.TryGetValue(BuiltInServerHelper.RagKey, out var ragSavedConfig)
            && ragSavedConfig.Folders.Count > 0)
        {
            // Folders[0] stores the KB ID in the new multi-KB model
            panel.KnowledgeArchiveFolder = ragSavedConfig.Folders[0];
        }

        // Set workspace capability based on profile
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        panel.IsWorkspaceEnabled = SelectedProfile?.EnabledServers.Contains(filesystemServerId) ?? false;

        // Set Knowledge Archive capability based on profile
        var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
        panel.IsKnowledgeArchiveEnabled = SelectedProfile?.EnabledServers.Contains(ragServerId) ?? false;

        // Memory text is now fetched from Essentials MCP at send time (PrepareToolsForSendAsync)

        // Relay keyboard shortcut from WebView2 (separate HWND)
        panel.KeyboardShortcutPressed += (s, e) => KeyboardShortcutPressed?.Invoke(s, e);

        // Relay notification requests (e.g., image saved/copied) to the app notification center
        panel.NotificationRequested += (_, args) =>
            NotificationCenter.Instance.Post(args.Title, args.Message);

        // Wire send-time tool resolution (auto-connect + connection filtering)
        panel.PrepareToolsForSendAsync = PrepareToolsForSendAsync;

        // Wire specialist callbacks for auto-approve and UI labeling
        panel.IsRegisteredSpecialist = name =>
            Specialists.SpecialistRegistry.Instance.TryGet(name, out _);
        panel.SpecialistDisplayNameResolver = name =>
            Specialists.SpecialistRegistry.Instance.TryGet(name, out var s) ? s.DisplayName : null;

        // Flush branch messages first (tree-only, not active path)
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId, timestamp) in _pendingBranchMessages)
        {
            var msg = id is not null
                ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = modelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Timestamp = timestamp ?? DateTime.UtcNow }
                : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = modelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Timestamp = timestamp ?? DateTime.UtcNow };
            panel.RegisterBranchMessage(msg);
        }
        _pendingBranchMessages.Clear();

        // Flush active path messages
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary) in _pendingMessages)
        {
            panel.AddRestoredMessage(role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary);
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
        string? activeChildId = null,
        IReadOnlyList<ChatAttachment>? attachments = null,
        IReadOnlyList<MediaContent>? toolMedia = null,
        string? thinkingContent = null,
        DateTime? timestamp = null,
        double? elapsedSeconds = null,
        int? tokenCount = null,
        SummaryMeta? summary = null)
    {
        if (Panel is not null)
            Panel.AddRestoredMessage(role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary);
        else
            _pendingMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary));
    }

    /// <summary>
    /// Registers a message in the conversation tree without adding to the active path.
    /// Used for loading inactive branch messages from saved conversations.
    /// </summary>
    public void RegisterBranchMessage(ChatRole role, string content, string? providerName, string? providerModelId,
        string? id = null, string? parentId = null,
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null,
        string? activeChildId = null,
        DateTime? timestamp = null)
    {
        if (Panel is not null)
        {
            var msg = id is not null
                ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Timestamp = timestamp ?? DateTime.UtcNow }
                : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Timestamp = timestamp ?? DateTime.UtcNow };
            Panel.RegisterBranchMessage(msg);
        }
        else
        {
            _pendingBranchMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId, timestamp));
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
    /// Used when saving the conversation to .astx file.
    /// </summary>
    public Dictionary<string, BuiltInServerConfig>? GetBuiltInServers() => _builtInServers;

    /// <summary>
    /// Sets the built-in server configurations (used when loading from .astx file).
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
            foreach (var obj in presets)
            {
                if (obj is not ProviderPreset p) continue;
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
            if (presets.OfType<ProviderPreset>().FirstOrDefault() is { } first)
            {
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

    /// <summary>
    /// Handles memory store changes to refresh the memory text in the panel.
    /// </summary>
    // Memory text is now fetched from Essentials MCP at send time — no event handler needed.

    /// <inheritdoc/>
    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // Unsubscribe from profile events
        if (SelectedProfile is not null)
            SelectedProfile.ToolSettingsChanged -= OnProfileToolSettingsChanged;

        // Memory store events removed — memory fetched from MCP at send time

        // Disconnect per-tab MCP servers
        if (_filesystemConnection is not null)
        {
            _ = App.McpRegistry.RemoveAsync(_filesystemConnection);
            _filesystemConnection = null;
        }
        // RAG is shared — no per-tab cleanup needed

        if (AppTasksProvider is IDisposable utilDisposable)
        {
            utilDisposable.Dispose();
        }
        if (Provider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        // Close WebView2 to prevent ghosting on container recycling
        Panel?.Dispose();
        Panel = null;
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

        // Invalidate sub-agent tool so it picks up the new default preset
        _subAgentTool = null;

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
        AppendSpecialistGuideline(profile);

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

            // Knowledge Archive capability (shared server — no per-tab connect/disconnect)
            var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
            Panel.IsKnowledgeArchiveEnabled = profile.EnabledServers.Contains(ragServerId);
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
        AppendSpecialistGuideline(profile);

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
    /// Handles Knowledge Base selection changes from the title bar flyout.
    /// Saves the selected KB ID to conversation data. RAG is a shared server —
    /// no per-tab connect/disconnect needed.
    /// </summary>
    public void OnKnowledgeArchiveFolderChanged(object? _sender, string? kbId)
    {
        _builtInServers ??= [];

        if (!string.IsNullOrEmpty(kbId))
        {
            _builtInServers[BuiltInServerHelper.RagKey] = new BuiltInServerConfig
            {
                IsEnabled = true,
                Folders = [kbId], // Stores KB ID
            };
        }
        else
        {
            _builtInServers.Remove(BuiltInServerHelper.RagKey);
        }
        IsDirty = true;

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

    // RAG is now a shared multi-KB server — per-tab ConnectRagAsync/DisconnectRagAsync removed.
    // Indexing is managed via Settings > Knowledge Bases page (RagProcessManager.StartExec).

    /// <summary>
    /// Resolves registered tools for Flyout display (connection-independent).
    /// Actual connection filtering happens at send time via <see cref="PrepareToolsForSendAsync"/>.
    /// </summary>
    private void ResolveTools(Profile? profile)
    {
        var tools = new List<IAssistTool>();
        var enabledServerIds = profile?.EnabledServers ?? [];
        var enabledSet = enabledServerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 1. Essentials — real MCP server (placeholder for auto-connect at send time)
        var essentialsId = $"builtin_{BuiltInServerHelper.EssentialsKey}";
        if (enabledSet.Contains(BuiltInServerHelper.EssentialsKey)
            || enabledSet.Contains(essentialsId))
        {
            tools.Add(new ServerPlaceholderTool
            {
                Name = essentialsId,
                DisplayName = BuiltInServerHelper.EssentialsDisplayName,
            });
        }

        // 2. search_tools — when profile has enabled MCP servers
        var effectiveServerIds = BuildEffectiveServerIds(enabledServerIds);

        if (effectiveServerIds.Count > 0)
        {
            var searchTool = ToolRegistry.Resolve(["search_tools"]).FirstOrDefault();
            if (searchTool is not null)
            {
                if (searchTool is ISearchToolScope scoped)
                {
                    var allowedToolNames = GetToolNamesFromServers(effectiveServerIds);
                    scoped.AllowedToolNames = allowedToolNames.Count > 0 ? allowedToolNames : null;
                }

                tools.Add(searchTool);
            }
        }

        // 3. Server placeholders — for flyout display
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

        var outboxId = $"builtin_{BuiltInServerHelper.OutboxKey}";
        if (enabledSet.Contains(outboxId))
        {
            tools.Add(new ServerPlaceholderTool
            {
                Name = outboxId,
                DisplayName = BuiltInServerHelper.OutboxDisplayName,
            });
        }

        var runnerId = $"builtin_{BuiltInServerHelper.RunnerKey}";
        if (enabledSet.Contains(runnerId))
        {
            tools.Add(new ServerPlaceholderTool
            {
                Name = runnerId,
                DisplayName = BuiltInServerHelper.RunnerDisplayName,
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

        // Cache values for thread-safe access by SubAgent (runs on ThreadPool)
        _parentEnabledServers = [.. enabledServerIds];
        _cachedKbId = Panel?.KnowledgeArchiveFolder;

        // Sub-Agent tool — always available (works without tools for text-only tasks)
        tools.Add(GetOrCreateSubAgentTool());

        RegisteredTools = tools;

        // McpTools are resolved at send time by PrepareToolsForSendAsync
        McpTools = [];
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

        // 1+2. Auto-connect and determine connected servers
        var connectedIds = await AutoConnectServersAsync(effectiveServerIds);

        // 3. Build final tool list from real tools + connected server tools
        var result = new List<IAssistTool>();
        foreach (var tool in realTools)
        {
            if (tool.Name == "search_tools")
            {
                if (connectedIds.Count > 0)
                {
                    if (tool is ISearchToolScope scoped)
                        scoped.AllowedToolNames = GetToolNamesFromServers(connectedIds);
                    result.Add(tool);
                }
            }
            else
            {
                // Pass through non-placeholder, non-search_tools tools (e.g. delegate_task)
                result.Add(tool);
            }
        }

        // 4. Add directly-exposed tools from connected servers
        CollectToolsFromConnectedServers(connectedIds, result);

        // 5. Refresh memory text from Essentials server
        var essentialsConn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (essentialsConn?.IsConnected == true)
        {
            try
            {
                var memoryText = await FetchMemoryTextAsync(essentialsConn);
                if (Panel is not null)
                    Panel.MemoryText = memoryText;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning($"[Send] Failed to fetch memory: {ex.Message}");
            }
        }

        // 6. Update McpTools for ToolCallExecutor (search_tools-discovered tools)
        McpTools = connectedIds.Count > 0
            ? [.. GetToolsFromServers(connectedIds)]
            : [];

        // Sync to ChatPanel so ToolCallExecutor can find search_tools-discovered tools
        if (Panel is not null)
            Panel.McpTools = McpTools;

        return result;
    }

    /// <summary>
    /// Resolves tools for a sub-agent session by connecting MCP servers and collecting tools.
    /// Matches <see cref="FieldCure.Ai.Execution.SubAgentExecutor.ToolResolver"/> delegate signature.
    /// </summary>
    private async Task<IReadOnlyList<IAssistTool>> ResolveToolsForSubAgentAsync(
        IReadOnlyList<string>? mcpServers,
        IReadOnlyList<string>? allowedTools,
        CancellationToken ct)
    {
        // Always start with parent conversation's enabled servers as base.
        // AI-specified mcp_servers are merged (union), never replacing parent servers.
        // This prevents the AI from accidentally excluding servers it doesn't know about (e.g., RAG).
        // Uses _parentEnabledServers (cached on UI thread) instead of Panel.SelectedProfile
        // to avoid COMException when called from ThreadPool.
        var merged = new HashSet<string>(
            _parentEnabledServers,
            StringComparer.OrdinalIgnoreCase);

        if (mcpServers is { Count: > 0 })
        {
            foreach (var id in mcpServers.Select(NormalizeServerId))
                merged.Add(id);
        }

        if (merged.Count == 0)
            return [];

        IEnumerable<string> serverIds = merged;

        var effectiveServerIds = BuildEffectiveServerIds(serverIds);
        var connectedIds = await AutoConnectServersAsync(effectiveServerIds);

        if (connectedIds.Count == 0)
            return [];

        var tools = new List<IAssistTool>();
        CollectToolsFromConnectedServers(connectedIds, tools);

        // Apply allowlist filter if AI specified allowed_tools
        if (allowedTools is { Count: > 0 })
        {
            var allowed = allowedTools.ToHashSet(StringComparer.OrdinalIgnoreCase);
            tools.RemoveAll(t => !allowed.Contains(t.Name));
        }

        // No-nesting: always remove delegate_task from sub-agent tools
        tools.RemoveAll(t => t.Name == "delegate_task");

        return tools;
    }

    /// <summary>
    /// Auto-connects disconnected servers and returns the set of connected server IDs.
    /// Shared by <see cref="PrepareToolsForSendAsync"/> and <see cref="ResolveToolsForSubAgentAsync"/>.
    /// </summary>
    private async Task<HashSet<string>> AutoConnectServersAsync(IEnumerable<string> serverIds)
    {
        foreach (var serverId in serverIds)
        {
            var conn = ResolveConnection(serverId);

            // On-demand connect for shared built-in servers not yet in registry
            if (conn is null && serverId.StartsWith("builtin_", StringComparison.Ordinal))
            {
                var serverKey = serverId["builtin_".Length..];
                if (BuiltInServerHelper.IsSharedServer(serverKey))
                {
                    try
                    {
                        var config = new BuiltInServerConfig { IsEnabled = true, Folders = [] };
                        await App.McpRegistry.ConnectBuiltInAsync(serverKey, config);
                        conn = ResolveConnection(serverId);
                        LoggingService.LogInfo($"[Send] On-demand connected shared server: {serverId}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning($"[Send] Failed to on-demand connect {serverId}: {ex.Message}");
                    }
                }
            }

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

        var connectedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var serverId in serverIds)
        {
            var conn = ResolveConnection(serverId);
            if (conn?.IsConnected == true)
                connectedIds.Add(serverId);
        }
        return connectedIds;
    }

    /// <summary>
    /// Collects tools from connected MCP servers into the result list.
    /// Applies priority ordering: Filesystem > RAG > Outbox > Runner > Essentials (with dedup).
    /// Shared by <see cref="PrepareToolsForSendAsync"/> and <see cref="ResolveToolsForSubAgentAsync"/>.
    /// </summary>
    private void CollectToolsFromConnectedServers(HashSet<string> connectedIds, List<IAssistTool> result)
    {
        // Filesystem tools — per-tab connection (higher priority than Essentials)
        if (_filesystemConnection?.IsConnected == true
            && connectedIds.Contains(_filesystemConnection.Config.Id))
        {
            foreach (var tool in _filesystemConnection.Tools)
                result.Add(tool);
        }

        // RAG tools — shared connection from McpRegistry
        var ragConn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RagKey);
        if (ragConn?.IsConnected == true
            && connectedIds.Contains(ragConn.Config.Id))
        {
            foreach (var ragTool in ragConn.Tools)
                result.Add(ragTool);
        }

        // Outbox tools — shared connection from McpRegistry
        var outboxConn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.OutboxKey);
        if (outboxConn?.IsConnected == true
            && connectedIds.Contains(outboxConn.Config.Id))
        {
            foreach (var tool in outboxConn.Tools)
                result.Add(tool);
        }

        // Runner tools — shared connection from McpRegistry
        var runnerConn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.RunnerKey);
        if (runnerConn?.IsConnected == true
            && connectedIds.Contains(runnerConn.Config.Id))
        {
            foreach (var tool in runnerConn.Tools)
                result.Add(tool);
        }

        // Essentials tools — shared connection from McpRegistry (lowest priority, dedup)
        var essentialsConn = App.McpRegistry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (essentialsConn?.IsConnected == true
            && connectedIds.Contains(essentialsConn.Config.Id))
        {
            var existingToolNames = new HashSet<string>(result.Select(t => t.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var tool in essentialsConn.Tools)
            {
                // Skip duplicate tool names — stateful servers (Filesystem) take precedence
                if (existingToolNames.Contains(tool.Name))
                    continue;
                result.Add(tool);
            }
        }
    }

    /// <summary>
    /// Fetches memory entries from Essentials MCP server and formats for system prompt injection.
    /// Returns null if no memories exist.
    /// </summary>
    private static async Task<string?> FetchMemoryTextAsync(Mcp.McpServerConnection essentialsConn)
    {
        var listMemoriesTool = essentialsConn.Tools.FirstOrDefault(t => t.Name == "list_memories");
        if (listMemoriesTool is null)
            return null;

        var args = System.Text.Json.JsonDocument.Parse("{\"limit\": 50}").RootElement;
        var resultJson = await essentialsConn.CallToolWithProgressAsync("list_memories", args, null, CancellationToken.None);

        using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("memories", out var memories) || memories.GetArrayLength() == 0)
            return null;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## User Memory");
        sb.AppendLine("The following information has been saved from previous conversations:");
        foreach (var entry in memories.EnumerateArray())
        {
            var key = entry.GetProperty("key").GetString();
            var value = entry.GetProperty("value").GetString();
            sb.AppendLine($"- [{key}] {value}");
        }

        return sb.ToString().TrimEnd();
    }

    #region Tool Resolution Helpers

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
        return effective;
    }

    /// <summary>
    /// Normalizes a server identifier from AI input (display name or partial ID) to the canonical ID.
    /// E.g., "Essentials" → "builtin_essentials", "rag" → "builtin_rag".
    /// Already-canonical IDs pass through unchanged.
    /// </summary>
    private static string NormalizeServerId(string input)
    {
        // Already a canonical builtin ID
        if (input.StartsWith("builtin_", StringComparison.OrdinalIgnoreCase))
            return input;

        // Try matching by display name or server key (case-insensitive)
        var trimmed = input.Trim();
        foreach (var key in new[] {
            BuiltInServerHelper.EssentialsKey,
            BuiltInServerHelper.FilesystemKey,
            BuiltInServerHelper.RagKey,
            BuiltInServerHelper.OutboxKey,
            BuiltInServerHelper.RunnerKey })
        {
            var displayName = BuiltInServerHelper.GetDisplayName(key);
            if (trimmed.Equals(key, StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals(displayName, StringComparison.OrdinalIgnoreCase))
            {
                return $"builtin_{key}";
            }
        }

        // External server — try matching by connection name
        var conn = App.McpRegistry.Connections.FirstOrDefault(c =>
            c.Config.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (conn is not null)
            return conn.Config.Id;

        // Unknown — pass through as-is
        return input;
    }

    /// <summary>Resolves a server ID to its <see cref="McpServerConnection"/>.</summary>
    private McpServerConnection? ResolveConnection(string serverId)
    {
        if (_filesystemConnection?.Config.Id == serverId) return _filesystemConnection;
        return App.McpRegistry.Connections.FirstOrDefault(c => c.Config.Id == serverId);
    }

    /// <summary>Finds the connection that owns the given tool.</summary>
    private McpServerConnection? FindConnectionForTool(IAssistTool tool)
    {
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

    #region Specialist Helpers

    /// <summary>
    /// Appends the specialist routing guideline to the system prompt
    /// if the profile has Essentials enabled and the specialist setting is on.
    /// </summary>
    private void AppendSpecialistGuideline(FieldCure.AssistStudio.Models.Profile profile)
    {
        if (!AppSettings.WebSearchSpecialistEnabled)
            return;

        var essentialsId = $"builtin_{BuiltInServerHelper.EssentialsKey}";
        if (!profile.EnabledServers.Contains(essentialsId))
            return;

        SystemPrompt += "\n\n" + Specialists.WebSearchSpecialist.RoutingGuideline;
    }

    #endregion

    #region Sub-Agent Helpers

    /// <summary>
    /// Returns the cached sub-agent tool, creating it on first access.
    /// Invalidate by setting <see cref="_subAgentTool"/> to <c>null</c> on preset change.
    /// </summary>
    private SubAgentTool GetOrCreateSubAgentTool()
    {
        return _subAgentTool ??= new SubAgentTool(
            new SubAgentExecutor(
                new AgentLoop(),
                ResolveProviderByName,
                ResolveToolsForSubAgentAsync,
                SelectedPreset?.Name ?? "Default"),
            () => _cachedKbId,
            Specialists.SpecialistRegistry.Instance,
            () => AppSettings.WebSearchSpecialistPreset);
    }

    /// <summary>
    /// Resolves a provider preset name to an <see cref="IAiProvider"/> instance.
    /// Used as <see cref="SubAgentExecutor.ProviderResolver"/> delegate.
    /// </summary>
    private IAiProvider ResolveProviderByName(string presetName)
    {
        if (AvailablePresets is not null)
        {
            foreach (var obj in AvailablePresets)
            {
                if (obj is ProviderPreset p && p.Name == presetName)
                    return ProviderFactory.Create(p);
            }
        }

        return Provider ?? throw new InvalidOperationException(
            $"No provider found for preset '{presetName}' and no fallback available.");
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

        foreach (var obj in availablePresets)
        {
            if (obj is ProviderPreset p && p.Name == presetName)
                return ProviderFactory.Create(p);
        }
        return null;
    }

    #endregion
}
