using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using AssistStudio.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.Ai.Execution;
using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Export;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Core.Helpers;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections;
using System.Text.Json;

namespace AssistStudio.Modules.ViewModels;

/// <summary>
/// View model for a single conversation tab, managing provider state,
/// observable properties for ChatPanel binding, dirty state, and file association.
/// </summary>
public partial class ChatTabViewModel : ObservableObject, IDisposable
{
    /// <summary>Shared resource loader for localized strings used by this view model.</summary>
    private static readonly ResourceLoader Res = new();

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
    /// Latest "should the per-tab Filesystem MCP server be running?" intent. Written
    /// by every lifecycle trigger (<see cref="OnProfileChanged"/>, <see cref="OnEnabledToolsChanged"/>,
    /// <see cref="OnProfileToolSettingsChanged"/>, <see cref="OnWorkspaceFoldersChanged"/>,
    /// <see cref="AttachPanel"/>) right before they kick the reconciler. Read by
    /// <see cref="ReconcileFilesystemAsync"/>'s post-apply verification loop so a
    /// state change that arrived during the previous apply is not lost.
    /// </summary>
    /// <remarks>
    /// Marked <see langword="volatile"/> as a low-cost guarantee: in the current
    /// code base every reader and writer ends up on the UI synchronization
    /// context (no <c>ConfigureAwait(false)</c> on the awaits), but a future
    /// edit that drops the captured context elsewhere should not silently
    /// reintroduce a cross-thread visibility bug. The keyword documents the
    /// shared-flag intent more clearly than a comment alone.
    /// </remarks>
    private volatile bool _filesystemDesiredOn;

    /// <summary>
    /// Serializes per-tab Filesystem MCP server lifecycle work (connect, disconnect,
    /// reconnect with new folders). Without this, rapid clicks in the compose bar
    /// tool-selection flyout could spawn two concurrent connect tasks both writing
    /// <see cref="_filesystemConnection"/>, leaking the loser as an orphaned subprocess
    /// in the registry. Held only across the lifecycle method call; readers of
    /// <see cref="_filesystemConnection"/> elsewhere do not contend on it.
    /// </summary>
    private readonly SemaphoreSlim _filesystemLifecycleLock = new(1, 1);

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
    /// Cached Knowledge Base folder (kb_id) from UI thread for thread-safe access
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
    /// Auxiliary provider resolver for title, summary, and sub-agent tasks.
    /// Validates connectivity and falls back to the parent provider on failure.
    /// </summary>
    [ObservableProperty] private IAuxiliaryProviderResolver? _auxiliaryResolver;

    /// <summary>
    /// Model name for title generation. <see langword="null"/> means inherit from conversation.
    /// </summary>
    [ObservableProperty] private string? _titleModel;

    /// <summary>
    /// Model name for summary generation. <see langword="null"/> means inherit from conversation.
    /// </summary>
    [ObservableProperty] private string? _summaryModel;

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
    [ObservableProperty] private IList? _availableModels;

    /// <summary>
    /// The currently selected ProviderModel.
    /// </summary>
    [ObservableProperty] private ProviderModel? _selectedModel;

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
    /// Input token threshold for automatic summarization.
    /// </summary>
    [ObservableProperty] private int _maxInputTokens;

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
    public ProviderModel? CurrentPreset { get; private set; }

    #endregion

    #region Events

    /// <summary>
    /// Raised when the user switches provider preset via ComposeBar ComboBox.
    /// </summary>
    public event Action<ChatTabViewModel, ProviderModel>? ModelSwitched;

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
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId, string? ActiveChildId, IReadOnlyList<ChatAttachment>? Attachments, IReadOnlyList<MediaContent>? ToolMedia, string? ThinkingContent, DateTime? Timestamp, double? ElapsedSeconds, int? TokenCount, SummaryMeta? Summary, bool IsHidden, bool IsContinuation, StopReason StopReason, JsonElement? StructuredContent)> _pendingMessages = [];

    /// <summary>
    /// Branch-only messages queued before the ChatPanel is available.
    /// These are registered in the tree but not added to the active path.
    /// </summary>
    private readonly List<(ChatRole Role, string Content, string? ProviderName, string? ModelId, string? Id, string? ParentId, IReadOnlyList<ToolCall>? ToolCalls, string? ToolCallId, string? ActiveChildId, IReadOnlyList<ChatAttachment>? Attachments, IReadOnlyList<MediaContent>? ToolMedia, DateTime? Timestamp, bool IsHidden, bool IsContinuation, StopReason StopReason, JsonElement? StructuredContent)> _pendingBranchMessages = [];

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
        ProviderModel preset,
        string systemPrompt,
        ChatTheme theme,
        IList availablePresets,
        List<Profile> profiles,
        Profile? selectedProfile,
        int tabNumber = 0)
    {
        CurrentPreset = preset;
        // Title starts empty until either the auto-titler runs or the user
        // edits it. The header in ChatPanel falls back to the localized
        // greeting whenever Title is empty (regardless of conversation
        // activity), so transient mid-conversation states — e.g., a tool
        // approval panel that opens before the AI finishes its first turn —
        // still read as "fresh" instead of leaking the model name.
        _title = string.Empty;

        var prefix = Res.GetString("Tab_NewConversation");
        _tabHeader = tabNumber > 0 ? $"{prefix} {tabNumber}" : prefix;

        // Set observable fields — ChatTabView.xaml binds to these via x:Bind
        _provider = ProviderFactory.Create(preset);
        _auxiliaryResolver = new AuxiliaryProviderResolver(() => AvailableModels!);
        _titleModel = ResolveTaskPreset(AppSettings.TitleSource, AppSettings.TitleModel);
        _summaryModel = ResolveTaskPreset(AppSettings.SummarySource, AppSettings.SummaryModel);
        _systemPrompt = systemPrompt;
        _theme = theme;
        _availableModels = availablePresets;
        _selectedModel = preset;
        _availableProfiles = profiles;
        _selectedProfile = selectedProfile;
        _autoTitle = AppSettings.AppAutoTitle;
        _autoSummarize = AppSettings.AppAutoSummary;
        _maxInputTokens = AppSettings.AppMaxInputTokens;
#if DEBUG
        _isDebugMode = true;
#endif

        // Subscribe to task settings changes from Settings page
        AppSettings.TaskSettingsChanged += (_, _) =>
        {
            AutoTitle = AppSettings.AppAutoTitle;
            AutoSummarize = AppSettings.AppAutoSummary;
            MaxInputTokens = AppSettings.AppMaxInputTokens;
            TitleModel = ResolveTaskPreset(AppSettings.TitleSource, AppSettings.TitleModel);
            SummaryModel = ResolveTaskPreset(AppSettings.SummarySource, AppSettings.SummaryModel);
            _subAgentTool = null; // Invalidate cached sub-agent tool on settings change
        };

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
        var titleForLog = string.IsNullOrEmpty(Title) ? "(untitled)" : Title;
        LoggingService.LogInfo($"[Tab] Panel attached: {titleForLog} [{Id}], pending={_pendingMessages.Count}, initialized={panel.IsInitialized}");

        // Fresh panels also start with IsInitialized == false, so we only force
        // reinitialization for containers that were explicitly disposed and recycled.
        if (panel.NeedsWebViewReinitialization)
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
            panel.KnowledgeBaseId = ragSavedConfig.Folders[0];
        }

        // Set workspace capability based on profile
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        panel.IsWorkspaceEnabled = SelectedProfile?.EnabledServers.Contains(filesystemServerId) ?? false;

        // Set Knowledge Base capability based on profile
        var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
        panel.IsKnowledgeBaseEnabled = SelectedProfile?.EnabledServers.Contains(ragServerId) ?? false;

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

        // Flush branch messages first (tree-only, not active path).
        // Attachments and ToolMedia must be set on the inactive-branch ChatMessage
        // here, otherwise switching back to that branch later would render an
        // empty bubble (the branch's media wasn't restored from disk).
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, timestamp, isHidden, isContinuation, stopReason, structuredContent) in _pendingBranchMessages)
        {
            var msg = id is not null
                ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = modelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Attachments = attachments ?? [], ToolMedia = toolMedia, StructuredContent = structuredContent, Timestamp = timestamp ?? DateTime.UtcNow, IsHidden = isHidden, IsContinuation = isContinuation, StopReason = stopReason }
                : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = modelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Attachments = attachments ?? [], ToolMedia = toolMedia, StructuredContent = structuredContent, Timestamp = timestamp ?? DateTime.UtcNow, IsHidden = isHidden, IsContinuation = isContinuation, StopReason = stopReason };
            panel.RegisterBranchMessage(msg);
        }
        _pendingBranchMessages.Clear();

        // Flush active path messages
        foreach (var (role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary, isHidden, isContinuation, stopReason, structuredContent) in _pendingMessages)
        {
            panel.AddRestoredMessage(role, content, providerName, modelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary, isHidden, isContinuation, stopReason, structuredContent);
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

        // .astx restore is the fifth filesystem-lifecycle trigger (alongside
        // OnProfileChanged, OnEnabledToolsChanged, OnProfileToolSettingsChanged,
        // and OnWorkspaceFoldersChanged). At this point the saved state has
        // been applied: panel.IsWorkspaceEnabled reflects whether "filesystem"
        // is checked in the active profile, and panel.WorkspaceFolders holds
        // whatever the conversation had on save. The reconciler reads both
        // and brings the connection up if appropriate, on its own task — we
        // do not await here because that would suspend AttachPanel for the
        // duration of the dnx fetch + serve startup (1–3 s on a cold cache)
        // and visibly delay the force-push tools block above.
        RequestFilesystemReconcile();
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
        SummaryMeta? summary = null,
        bool isHidden = false,
        bool isContinuation = false,
        StopReason stopReason = StopReason.Completed,
        JsonElement? structuredContent = null)
    {
        if (Panel is not null)
            Panel.AddRestoredMessage(role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary, isHidden, isContinuation, stopReason, structuredContent);
        else
            _pendingMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, thinkingContent, timestamp, elapsedSeconds, tokenCount, summary, isHidden, isContinuation, stopReason, structuredContent));
    }

    /// <summary>
    /// Registers a message in the conversation tree without adding to the active path.
    /// Used for loading inactive branch messages from saved conversations.
    /// </summary>
    public void RegisterBranchMessage(ChatRole role, string content, string? providerName, string? providerModelId,
        string? id = null, string? parentId = null,
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null,
        string? activeChildId = null,
        IReadOnlyList<ChatAttachment>? attachments = null,
        IReadOnlyList<MediaContent>? toolMedia = null,
        DateTime? timestamp = null,
        bool isHidden = false,
        bool isContinuation = false,
        StopReason stopReason = StopReason.Completed,
        JsonElement? structuredContent = null)
    {
        if (Panel is not null)
        {
            var msg = id is not null
                ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Attachments = attachments ?? [], ToolMedia = toolMedia, StructuredContent = structuredContent, Timestamp = timestamp ?? DateTime.UtcNow, IsHidden = isHidden, IsContinuation = isContinuation, StopReason = stopReason }
                : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId, Attachments = attachments ?? [], ToolMedia = toolMedia, StructuredContent = structuredContent, Timestamp = timestamp ?? DateTime.UtcNow, IsHidden = isHidden, IsContinuation = isContinuation, StopReason = stopReason };
            Panel.RegisterBranchMessage(msg);
        }
        else
        {
            _pendingBranchMessages.Add((role, content, providerName, providerModelId, id, parentId, toolCalls, toolCallId, activeChildId, attachments, toolMedia, timestamp, isHidden, isContinuation, stopReason, structuredContent));
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
    /// Exports the active conversation branch as Markdown.
    /// Returns null if the panel is not available.
    /// </summary>
    public MarkdownExportResult? ExportToMarkdown() => Panel?.ExportToMarkdown();

    /// <summary>
    /// Gets all messages in the conversation tree (active path + all branches).
    /// Used for saving the full tree to disk.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetAllMessages()
    {
        return Panel?.GetAllMessages() ?? [];
    }

    /// <summary>
    /// Gets the built-in server configurations for this conversation (workspace folders, knowledge base).
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
    /// Applies a visual theme to the chat panel and refreshes the tab icon so the
    /// dirty-indicator brush (assigned from code via <see cref="ThemeHelper.GetBrush"/>)
    /// picks up the new theme's accent color.
    /// </summary>
    public void ApplyTheme(ChatTheme theme)
    {
        Theme = theme;
        OnPropertyChanged(nameof(TabIconSource));
    }

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
        AvailableModels = new ArrayList(presets);

        var found = false;
        if (currentName is not null)
        {
            foreach (var obj in presets)
            {
                if (obj is not ProviderModel p) continue;
                if (p.Name == currentName)
                {
                    found = true;
                    SelectedModel = p;

                    // Recreate provider if connection-relevant fields changed
                    if (p.ModelId != currentModelId ||
                        p.ApiKey != currentApiKey ||
                        p.BaseUrl != currentBaseUrl)
                    {
                        OnModelChanged(this, p);
                    }
                    break;
                }
            }
        }

        // Fallback: current preset was deleted — select the first available or clear
        if (!found)
        {
            if (presets.OfType<ProviderModel>().FirstOrDefault() is { } first)
            {
                SelectedModel = first;
                OnModelChanged(this, first);
            }
            else
            {
                CurrentPreset = null;
                SelectedModel = null;
            }
        }
    }

    /// <summary>
    /// Handles per-conversation tool toggles raised by the compose bar's
    /// tool-selection flyout. The flyout writes a list of enabled server names
    /// into <c>ChatPanel.EnabledToolNames</c> (or <see langword="null"/> when
    /// every server is enabled). All filesystem-lifecycle decisions go through
    /// the reconciler, which reads <see cref="ChatPanel.EnabledToolNames"/>
    /// live — passing the event argument here would be redundant and would
    /// risk drift if the property and event ever disagree.
    /// </summary>
    /// <param name="sender">Source <see cref="ChatPanel"/>.</param>
    /// <param name="enabled">New enabled-tool-name list (handled via reconciler reading live state).</param>
    public void OnEnabledToolsChanged(object? sender, IReadOnlyList<string>? enabled)
        => RequestFilesystemReconcile();

    /// <summary>
    /// Handles profile-level tool-settings changes (the per-server checkboxes
    /// in Settings → Profiles, which mutate <see cref="Profile.EnabledServers"/>
    /// directly). Differs from <see cref="OnEnabledToolsChanged"/>, which
    /// handles the per-conversation override in the compose bar. Both
    /// converge on <see cref="RequestFilesystemReconcile"/>.
    /// </summary>
    private void OnProfileToolSettingsChanged(object? sender, EventArgs e)
    {
        if (sender is not Profile profile) return;
        LoggingService.LogInfo($"[Tab] Profile.ToolSettingsChanged: {profile.Name}");
        RefreshTools();
        RequestFilesystemReconcile();
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
    public void OnModelChanged(object? _sender, ProviderModel preset)
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
        // Do not overwrite Title here. Switching the model mid-conversation
        // should not clobber an auto-generated or user-edited title.

        // Invalidate sub-agent tool so it picks up the new default preset
        _subAgentTool = null;

        ModelSwitched?.Invoke(this, preset);
    }

    /// <summary>
    /// Handles profile changes by updating the system prompt, workspace folders, and registered tools.
    /// Filesystem connection lifecycle goes through <see cref="RequestFilesystemReconcile"/>
    /// (the reconciler reads the just-updated <c>panel.IsWorkspaceEnabled</c>
    /// and live folder list).
    /// </summary>
    public void OnProfileChanged(object? _sender, Profile profile)
    {
        LoggingService.LogInfo($"[Tab] Profile changed: {profile.Name}");

        // Unsubscribe from old profile, subscribe to new
        if (SelectedProfile is not null)
            SelectedProfile.ToolSettingsChanged -= OnProfileToolSettingsChanged;
        profile.ToolSettingsChanged += OnProfileToolSettingsChanged;

        SystemPrompt = profile.SystemPrompt;
        AppendSpecialistGuideline(profile);

        // Update capability flags so the reconciler / UI both read consistent state.
        var filesystemServerId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var ragServerId = $"builtin_{BuiltInServerHelper.RagKey}";
        if (Panel is not null)
        {
            Panel.IsWorkspaceEnabled = profile.EnabledServers.Contains(filesystemServerId);
            Panel.IsKnowledgeBaseEnabled = profile.EnabledServers.Contains(ragServerId);
        }

        RequestFilesystemReconcile();

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
        var dialog = new ThemedContentDialog
        {
            Title = Res.GetString("Dialog_RenameConversation"),
            Content = input,
            PrimaryButtonText = Res.GetString("Dialog_OK"),
            CloseButtonText = Res.GetString("Dialog_Cancel"),
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
    /// Handles workspace folder changes from the title bar flyout. Saves the
    /// new folder list onto the conversation, then either:
    /// <list type="bullet">
    /// <item>updates the live MCP server's roots in-place when filesystem is
    /// already connected and folders remain non-empty (cheap, no subprocess
    /// restart), or</item>
    /// <item>defers to <see cref="RequestFilesystemReconcile"/> for any state
    /// transition that flips the desired connect/disconnect bit (first folder
    /// added, last folder removed).</item>
    /// </list>
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

        // Roots-update fast path: already connected with at least one folder
        // remaining means we are still in the "desiredOn=true" state and only
        // the folder set changed. Notify the running server via roots protocol
        // and avoid the reconciler's disconnect+reconnect cost.
        if (_filesystemConnection is not null
            && _filesystemConnection.IsConnected
            && folders.Count > 0)
        {
            await _filesystemConnection.UpdateWorkspaceFoldersAsync(folders);
        }
        else
        {
            // Any other state transition (first connect, last folder removed,
            // filesystem disabled in profile) goes through the reconciler.
            RequestFilesystemReconcile();
        }

        ResolveTools(Panel?.SelectedProfile);
    }

    /// <summary>
    /// Handles Knowledge Base selection changes from the title bar flyout.
    /// Saves the selected KB ID to conversation data. RAG is a shared server —
    /// no per-tab connect/disconnect needed.
    /// </summary>
    public void OnKnowledgeBaseIdChanged(object? _sender, string? kbId)
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
    /// Reads live state and computes whether the per-tab Filesystem MCP server
    /// should be running. Returns <see langword="true"/> iff filesystem appears
    /// in the conversation's effective tool list <i>and</i> at least one
    /// workspace folder is configured. The effective check layers
    /// <c>panel.IsWorkspaceEnabled</c> (profile says "filesystem allowed") on
    /// top of <c>panel.EnabledToolNames</c> (per-conversation override that may
    /// hide it). Pure read — does not touch the connection.
    /// </summary>
    private bool ComputeFilesystemDesiredOn()
    {
        if (Panel?.IsWorkspaceEnabled != true) return false;

        var enabled = Panel.EnabledToolNames;
        var fsId = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        // null means "no override → all profile servers enabled", so filesystem
        // is implicitly on. A non-null list is the strict subset to keep.
        if (enabled is not null && !enabled.Contains(fsId, StringComparer.OrdinalIgnoreCase))
            return false;

        return Panel.WorkspaceFolders is { Count: > 0 };
    }

    /// <summary>
    /// Single entry point for filesystem-lifecycle triggers. Records the
    /// current desired state into <see cref="_filesystemDesiredOn"/> and
    /// kicks off <see cref="ReconcileFilesystemAsync"/>. Multiple concurrent
    /// callers (e.g., rapid clicks in the compose-bar tool flyout) collapse
    /// safely: the last writer wins via the do-while verification loop
    /// inside the reconciler, and the <see cref="_filesystemLifecycleLock"/>
    /// semaphore prevents two AddAndConnectAsync calls from racing on the
    /// same per-tab id.
    /// </summary>
    private void RequestFilesystemReconcile()
    {
        _filesystemDesiredOn = ComputeFilesystemDesiredOn();
        LoggingService.LogInfo(
            $"[Tab] Filesystem reconcile requested: desired={_filesystemDesiredOn}, " +
            $"current={(_filesystemConnection is not null ? "connected" : "none")}");
        _ = ReconcileFilesystemAsync();
    }

    /// <summary>
    /// Drives <see cref="_filesystemConnection"/> toward
    /// <see cref="_filesystemDesiredOn"/>. The semaphore serializes lifecycle
    /// ops so two connect calls cannot race on the per-tab id. Loop re-checks
    /// desired after await; collapses superseded toggles into idempotent
    /// no-ops without releasing the lock. A small iteration cap turns a
    /// hypothetical reentry bug (a future edit that calls
    /// <see cref="RequestFilesystemReconcile"/> from inside an apply) into a
    /// loud warning instead of a hung reconciler.
    /// </summary>
    private async Task ReconcileFilesystemAsync()
    {
        const int MaxIterations = 8;
        await _filesystemLifecycleLock.WaitAsync();
        try
        {
            bool last;
            var iterations = 0;
            do
            {
                last = _filesystemDesiredOn;
                await ApplyFilesystemDesiredAsync(last);
                if (++iterations >= MaxIterations)
                {
                    // Either a user is flipping the toggle faster than apply
                    // completes (extremely unlikely past a couple of cycles)
                    // or the reconciler is being re-triggered from inside an
                    // apply. Either way, stop instead of looping forever and
                    // log so the cause is visible.
                    LoggingService.LogWarning(
                        $"[Tab] Filesystem reconcile capped at {MaxIterations} iterations " +
                        $"(last desired={_filesystemDesiredOn}); breaking to avoid lock starvation.");
                    break;
                }
            } while (last != _filesystemDesiredOn);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[Tab] Filesystem reconcile failed: {ex.Message}");
        }
        finally
        {
            _filesystemLifecycleLock.Release();
        }
    }

    /// <summary>
    /// Performs one apply step toward the given desired state. Idempotent:
    /// connect when off-but-want-on (and folders are present), disconnect
    /// when on-but-want-off, no-op otherwise.
    /// </summary>
    /// <param name="desiredOn">Target state for this apply pass.</param>
    private async Task ApplyFilesystemDesiredAsync(bool desiredOn)
    {
        if (desiredOn)
        {
            if (_filesystemConnection is null
                && Panel?.WorkspaceFolders is { Count: > 0 } folders)
            {
                await ConnectFilesystemAsync(folders);
            }
        }
        else
        {
            if (_filesystemConnection is not null)
            {
                var conn = _filesystemConnection;
                _filesystemConnection = null;
                await App.McpRegistry.RemoveAsync(conn);
            }
        }
    }

    /// <summary>
    /// Connects (or reconnects) the per-tab Filesystem MCP server with the folders
    /// that currently exist on disk.
    /// </summary>
    /// <remarks>
    /// <b>Lifecycle principle — KEEP THIS PRINCIPLE INTACT.</b>
    /// <para>
    /// The per-tab Filesystem subprocess exists if and only if <i>both</i> of:
    /// </para>
    /// <list type="number">
    /// <item><c>filesystem</c> is enabled in the conversation's effective tool
    /// list — that is, <c>profile.EnabledServers</c> contains
    /// <c>builtin_filesystem</c> <i>and</i> the per-conversation override
    /// (<see cref="ChatPanel.EnabledToolNames"/>) does not strip it out, AND</item>
    /// <item>at least one workspace folder is configured on the panel.</item>
    /// </list>
    /// <para>
    /// All lifecycle work goes through a single reconciler entry point,
    /// <see cref="RequestFilesystemReconcile"/>, which records the desired
    /// state into <see cref="_filesystemDesiredOn"/> and drives the connection
    /// toward it under <see cref="_filesystemLifecycleLock"/>. The five
    /// trigger sites listed below each just call <c>RequestFilesystemReconcile</c> —
    /// they do not call <c>ConnectFilesystemAsync</c> or <c>RemoveAsync</c>
    /// directly. Bypassing the reconciler is a layering violation that
    /// re-opens the rage-click subprocess-leak race.
    /// </para>
    /// <list type="bullet">
    /// <item><see cref="OnProfileChanged"/> — user switches profile.</item>
    /// <item><see cref="OnEnabledToolsChanged"/> — user toggles a server in
    /// the compose bar's tool-selection flyout (per-conversation override).</item>
    /// <item><see cref="OnProfileToolSettingsChanged"/> — user toggles a
    /// server in Settings → Profiles, mutating <c>profile.EnabledServers</c>.</item>
    /// <item><see cref="OnWorkspaceFoldersChanged"/> — user adds or removes a
    /// folder. Already-connected + folder-set-changed takes the cheap roots
    /// notification fast path; any state transition that flips the
    /// connect/disconnect bit goes through the reconciler.</item>
    /// <item><see cref="AttachPanel"/> — restoring an .astx whose saved state
    /// already satisfies both conditions.</item>
    /// </list>
    /// <para>
    /// Send-time logic in <see cref="PrepareToolsForSendAsync"/> is for
    /// <i>tool exposure</i>, not connection. It decides which already-connected
    /// servers' tools the model sees this turn; it does <b>not</b> spawn
    /// subprocesses lazily.
    /// </para>
    /// <para>
    /// Folders saved in <c>WorkspaceFolders</c> (and persisted in <c>.astx</c>) may
    /// no longer exist by the time the conversation is reopened — the user could
    /// have moved, renamed, or unmounted them. Missing folders are <b>kept</b> in
    /// <c>WorkspaceFolders</c> (and the <c>.astx</c>) so the user can still see
    /// them in the flyout (with a warning icon) and recover by restoring the disk
    /// path; only the live MCP server receives the filtered alive set, since the
    /// filesystem server either rejects missing folders at startup or fails every
    /// per-tool call against them.
    /// </para>
    /// <para>
    /// On a successful initial connection a single toast is posted so the user
    /// confirms "the workspace is now usable" without polling the UI. When some
    /// folders are missing the toast severity upgrades to <see cref="InfoBarSeverity.Warning"/>
    /// and the title appends " — missing: {N}". When every folder is missing,
    /// no connection is attempted and a Warning-only toast surfaces the situation.
    /// Subsequent folder additions go through <c>UpdateWorkspaceFoldersAsync</c>
    /// on the existing connection and intentionally do not re-toast.
    /// </para>
    /// </remarks>
    private async Task ConnectFilesystemAsync(IReadOnlyList<string> folders)
    {
        // Disconnect existing per-tab filesystem server
        if (_filesystemConnection is not null)
        {
            await App.McpRegistry.RemoveAsync(_filesystemConnection);
            _filesystemConnection = null;
        }

        if (folders.Count == 0) return;

        var aliveFolders = folders.Where(System.IO.Directory.Exists).ToList();
        var missingCount = folders.Count - aliveFolders.Count;

        if (aliveFolders.Count == 0)
        {
            // All saved folders missing — no connection to make. Surface this
            // explicitly so the user knows why the workspace is unreachable.
            NotificationCenter.Instance.Post(
                InfoBarSeverity.Warning,
                string.Format(Res.GetString("Mcp_WorkspaceFoldersAllMissing"), missingCount),
                string.Empty);
            return;
        }

        var mcpConfig = BuiltInServerHelper.CreateMcpServerConfig(
            BuiltInServerHelper.FilesystemKey,
            new BuiltInServerConfig { IsEnabled = true, Folders = [.. aliveFolders] });
        if (mcpConfig is null) return;

        // Unique ID per tab to allow multiple filesystem instances in the registry
        mcpConfig.Id = $"builtin_{BuiltInServerHelper.FilesystemKey}_{Id}";

        _filesystemConnection = await App.McpRegistry.AddAndConnectAsync(
            mcpConfig, supportsRoots: true);

        if (_filesystemConnection.IsConnected)
            PostFilesystemConnectedToast(aliveFolders, missingCount);
    }

    /// <summary>
    /// Posts the localized "workspace folder(s) connected" toast for an initial
    /// connect. Splits singular vs plural based on <paramref name="aliveFolders"/>
    /// (single-folder shows the last path segment, multi-folder shows the count
    /// because listing every name would overflow the infobar). Appends a
    /// " — missing: {N}" suffix and upgrades severity to Warning when any of
    /// the requested folders were missing on disk.
    /// </summary>
    /// <param name="aliveFolders">Folders that actually existed and were passed to the MCP server.</param>
    /// <param name="missingCount">Number of requested folders that were missing on disk (zero is the common case).</param>
    private static void PostFilesystemConnectedToast(IReadOnlyList<string> aliveFolders, int missingCount)
    {
        string title;
        if (aliveFolders.Count == 1)
        {
            // Trim trailing separators so "C:\Users\foo\Documents\" still yields "Documents".
            var trimmed = aliveFolders[0].TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
            var name = System.IO.Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) name = trimmed; // root-only path (e.g. "C:\")
            title = string.Format(Res.GetString("Mcp_WorkspaceFolderConnected"), name);
        }
        else
        {
            title = string.Format(Res.GetString("Mcp_WorkspaceFoldersConnected"), aliveFolders.Count);
        }

        if (missingCount > 0)
            title += string.Format(Res.GetString("Mcp_WorkspaceFoldersMissingSuffix"), missingCount);

        NotificationCenter.Instance.Post(
            missingCount > 0 ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
            title,
            string.Empty);
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

        // 2. search_tools + invoke_tool — when profile has enabled MCP servers.
        // search_tools is discovery; invoke_tool is the dispatcher that lets
        // Claude-class models actually call tools surfaced by search_tools
        // (without it, strict manifest-adherent models cannot emit tool_use for
        // tools that live only in McpTools). ToolCallExecutor unwraps invoke_tool
        // calls and re-dispatches through the normal confirmation/fallback path.
        var effectiveServerIds = BuildEffectiveServerIds(enabledServerIds);

        if (effectiveServerIds.Count > 0)
        {
            var allowedToolNames = GetToolNamesFromServers(effectiveServerIds);
            var scopeSet = allowedToolNames.Count > 0 ? allowedToolNames : null;

            foreach (var metaName in new[] { "search_tools", "invoke_tool" })
            {
                var metaTool = ToolRegistry.Resolve([metaName]).FirstOrDefault();
                if (metaTool is null) continue;

                if (metaTool is ISearchToolScope scoped)
                    scoped.AllowedToolNames = scopeSet;

                tools.Add(metaTool);
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
        _cachedKbId = Panel?.KnowledgeBaseId;

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

        // 3. Build final tool list from real tools + connected server tools.
        // search_tools and invoke_tool form a discovery/dispatch pair and both
        // require a live connection to be useful; they are dropped together
        // when no MCP servers are connected this turn.
        var result = new List<IAssistTool>();
        var connectedToolNames = connectedIds.Count > 0
            ? GetToolNamesFromServers(connectedIds)
            : null;

        foreach (var tool in realTools)
        {
            if (tool.Name is "search_tools" or "invoke_tool")
            {
                if (connectedIds.Count > 0)
                {
                    if (tool is ISearchToolScope scoped)
                        scoped.AllowedToolNames = connectedToolNames;
                    result.Add(tool);
                }
            }
            else
            {
                // Pass through other meta tools (e.g. delegate_task)
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
    /// Matches <see cref="SubAgentExecutor.ToolResolver"/> delegate signature.
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
    private static async Task<string?> FetchMemoryTextAsync(McpServerConnection essentialsConn)
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

    #endregion

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
    /// Appends specialist routing guidelines to the system prompt.
    /// Specialists are self-contained — each declares its own
    /// <see cref="ISpecialist.FallbackServers"/> / <see cref="ISpecialist.AllowedTools"/>
    /// which the sub-agent merges in regardless of the parent profile, so
    /// guidelines are injected independent of which servers the parent has enabled.
    /// </summary>
    private void AppendSpecialistGuideline(Profile profile)
    {
        if (AppSettings.WebSearchSpecialistEnabled)
            SystemPrompt += "\n\n" + Specialists.WebSearchSpecialist.RoutingGuideline;

        SystemPrompt += "\n\n" + Specialists.JudgmentRoutingGuide.RoutingGuideline;
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
                new AgentLoop { LogCallback = DiagnosticLogger.LogInfo },
                ResolveProviderForSubAgentAsync,
                ResolveToolsForSubAgentAsync),
            () => _cachedKbId,
            Specialists.SpecialistRegistry.Instance,
            AppSettings.ResolveSpecialistModel,
            () => ResolveTaskPreset(AppSettings.SubAgentSource, AppSettings.SubAgentModel));
    }

    /// <summary>
    /// Async provider resolver for sub-agent execution.
    /// Uses the auxiliary resolver for connectivity validation and fallback.
    /// </summary>
    /// <remarks>
    /// Resolution order:
    /// <list type="number">
    ///   <item>If <paramref name="presetName"/> is non-null (specialist override or explicit), use it.</item>
    ///   <item>Else, use the Sub-Agent per-task setting (Inherit → parent, Specific → that preset).</item>
    /// </list>
    /// All paths go through <see cref="IAuxiliaryProviderResolver"/> for fallback on failure.
    /// </remarks>
    /// <summary>
    /// Resolves a provider for sub-agent execution. PresetName is fully resolved
    /// by <see cref="SubAgentTool"/> before reaching here — this method just
    /// delegates to the auxiliary resolver for preset lookup and fallback.
    /// </summary>
    private async Task<IAiProvider> ResolveProviderForSubAgentAsync(string? presetName, CancellationToken ct)
    {
        var parentProvider = Provider ?? throw new InvalidOperationException(
            "No parent provider available for sub-agent fallback.");

        if (AuxiliaryResolver is { } resolver)
            return await resolver.ResolveWithFallbackAsync(presetName, parentProvider, "SubAgent", ct);

        return parentProvider;
    }

    /// <summary>
    /// Resolves a per-task preset name from the source/preset pair in AppSettings.
    /// Returns <see langword="null"/> for "Inherit" (meaning use parent provider).
    /// </summary>
    private static string? ResolveTaskPreset(string source, string preset)
        => source == "Specific" && !string.IsNullOrEmpty(preset) ? preset : null;

    #endregion
}
