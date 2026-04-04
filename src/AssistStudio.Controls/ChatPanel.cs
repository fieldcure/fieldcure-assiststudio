using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Rendering;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Channels;
using FieldCure.AssistStudio.Controls.Helpers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Theme mode for the ChatPanel.
/// </summary>
public enum ChatTheme
{
    /// <summary>Follow the system/app theme (default).</summary>
    System,
    /// <summary>Force light theme.</summary>
    Light,
    /// <summary>Force dark theme.</summary>
    Dark
}

/// <summary>
/// A templated control that provides a complete chat experience with message streaming,
/// attachments, preset selection, and conversation management. Default style is defined in Generic.xaml.
/// </summary>
public sealed partial class ChatPanel : Control
{
    private static readonly Windows.ApplicationModel.Resources.ResourceLoader Res =
        new("AssistStudio.Controls/Resources");

    #region Dependency Properties

    /// <summary>Identifies the <see cref="Provider"/> dependency property.</summary>
    public static readonly DependencyProperty ProviderProperty =
        DependencyProperty.Register(nameof(Provider), typeof(IAiProvider), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Placeholder"/> dependency property.</summary>
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(ChatPanel),
            new PropertyMetadata("Type a message..."));

    /// <summary>Identifies the <see cref="SystemPrompt"/> dependency property.</summary>
    public static readonly DependencyProperty SystemPromptProperty =
        DependencyProperty.Register(nameof(SystemPrompt), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="MemoryText"/> dependency property.</summary>
    public static readonly DependencyProperty MemoryTextProperty =
        DependencyProperty.Register(nameof(MemoryText), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="Theme"/> dependency property.</summary>
    public static readonly DependencyProperty ThemeProperty =
        DependencyProperty.Register(nameof(Theme), typeof(ChatTheme), typeof(ChatPanel),
            new PropertyMetadata(ChatTheme.System, OnThemePropertyChanged));

    /// <summary>Identifies the <see cref="AvailablePresets"/> dependency property.</summary>
    public static readonly DependencyProperty AvailablePresetsProperty =
        DependencyProperty.Register(nameof(AvailablePresets), typeof(IList), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailablePresetsChanged));

    /// <summary>Identifies the <see cref="SelectedPreset"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedPresetProperty =
        DependencyProperty.Register(nameof(SelectedPreset), typeof(ProviderPreset), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedPresetChanged));

    /// <summary>Identifies the <see cref="AvailableProfiles"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableProfilesProperty =
        DependencyProperty.Register(nameof(AvailableProfiles), typeof(IList<Profile>), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailableProfilesChanged));

    /// <summary>Identifies the <see cref="SelectedProfile"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedProfileProperty =
        DependencyProperty.Register(nameof(SelectedProfile), typeof(Profile), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedProfileChanged));

    /// <summary>Identifies the <see cref="IsDebugMode"/> dependency property.</summary>
    public static readonly DependencyProperty IsDebugModeProperty =
        DependencyProperty.Register(nameof(IsDebugMode), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false, OnIsDebugModeChanged));

    /// <summary>Identifies the <see cref="Title"/> dependency property.</summary>
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null, OnTitlePropertyChanged));

    /// <summary>Identifies the <see cref="AutoTitle"/> dependency property.</summary>
    public static readonly DependencyProperty AutoTitleProperty =
        DependencyProperty.Register(nameof(AutoTitle), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="AutoSummarize"/> dependency property.</summary>
    public static readonly DependencyProperty AutoSummarizeProperty =
        DependencyProperty.Register(nameof(AutoSummarize), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="MaxInputTokens"/> dependency property.</summary>
    public static readonly DependencyProperty MaxInputTokensProperty =
        DependencyProperty.Register(nameof(MaxInputTokens), typeof(int), typeof(ChatPanel),
            new PropertyMetadata(0));

    /// <summary>Identifies the <see cref="MaxToolCallRounds"/> dependency property.</summary>
    public static readonly DependencyProperty MaxToolCallRoundsProperty =
        DependencyProperty.Register(nameof(MaxToolCallRounds), typeof(int), typeof(ChatPanel),
            new PropertyMetadata(10));



    /// <summary>Identifies the <see cref="RecentTurnsToKeep"/> dependency property.</summary>
    public static readonly DependencyProperty RecentTurnsToKeepProperty =
        DependencyProperty.Register(nameof(RecentTurnsToKeep), typeof(int), typeof(ChatPanel),
            new PropertyMetadata(10));

    /// <summary>Identifies the <see cref="UtilityProvider"/> dependency property.</summary>
    public static readonly DependencyProperty UtilityProviderProperty =
        DependencyProperty.Register(nameof(UtilityProvider), typeof(IAiProvider), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="WorkspaceContext"/> dependency property.</summary>
    public static readonly DependencyProperty WorkspaceContextProperty =
        DependencyProperty.Register(nameof(WorkspaceContext), typeof(IWorkspaceContext), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="ContextProvider"/> dependency property.</summary>
    public static readonly DependencyProperty ContextProviderProperty =
        DependencyProperty.Register(nameof(ContextProvider), typeof(IContextProvider), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="RegisteredTools"/> dependency property.</summary>
    public static readonly DependencyProperty RegisteredToolsProperty =
        DependencyProperty.Register(nameof(RegisteredTools), typeof(IReadOnlyList<IAssistTool>), typeof(ChatPanel),
            new PropertyMetadata(null, OnRegisteredToolsChanged));

    /// <summary>Identifies the <see cref="McpTools"/> dependency property.</summary>
    public static readonly DependencyProperty McpToolsProperty =
        DependencyProperty.Register(nameof(McpTools), typeof(IReadOnlyList<IAssistTool>), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="FontFamily"/> dependency property for chat rendering.</summary>
    public new static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.Register("ChatFontFamily", typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null, OnFontFamilyChanged));

    /// <summary>Identifies the <see cref="FontSize"/> dependency property for chat rendering.</summary>
    public new static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.Register("ChatFontSize", typeof(double), typeof(ChatPanel),
            new PropertyMetadata(15.0, OnFontSizeChanged));

    /// <summary>Identifies the <see cref="IsReadOnly"/> dependency property.</summary>
    public static readonly DependencyProperty IsReadOnlyProperty =
        DependencyProperty.Register(nameof(IsReadOnly), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false, OnIsReadOnlyChanged));

    /// <summary>Identifies the <see cref="ShowTitleBar"/> dependency property.</summary>
    public static readonly DependencyProperty ShowTitleBarProperty =
        DependencyProperty.Register(nameof(ShowTitleBar), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(true, OnShowTitleBarChanged));


    /// <summary>Identifies the <see cref="WorkspaceFolders"/> dependency property.</summary>
    public static readonly DependencyProperty WorkspaceFoldersProperty =
        DependencyProperty.Register(nameof(WorkspaceFolders), typeof(IReadOnlyList<string>), typeof(ChatPanel),
            new PropertyMetadata(null, OnWorkspaceFoldersChanged));

    /// <summary>Identifies the <see cref="IsWorkspaceEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsWorkspaceEnabledProperty =
        DependencyProperty.Register(nameof(IsWorkspaceEnabled), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(true, OnIsWorkspaceEnabledChanged));

    /// <summary>Identifies the <see cref="KnowledgeArchiveFolder"/> dependency property.</summary>
    public static readonly DependencyProperty KnowledgeArchiveFolderProperty =
        DependencyProperty.Register(nameof(KnowledgeArchiveFolder), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null, OnKnowledgeArchiveFolderChanged));

    private static void OnKnowledgeArchiveFolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
            panel.UpdateFolderButtonBadge();
    }

    /// <summary>Identifies the <see cref="IsKnowledgeArchiveEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsKnowledgeArchiveEnabledProperty =
        DependencyProperty.Register(nameof(IsKnowledgeArchiveEnabled), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="IsArchiveIndexing"/> dependency property.</summary>
    public static readonly DependencyProperty IsArchiveIndexingProperty =
        DependencyProperty.Register(nameof(IsArchiveIndexing), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="ArchiveIndexingProgress"/> dependency property.</summary>
    public static readonly DependencyProperty ArchiveIndexingProgressProperty =
        DependencyProperty.Register(nameof(ArchiveIndexingProgress), typeof(double), typeof(ChatPanel),
            new PropertyMetadata(0.0));

    /// <summary>Identifies the <see cref="ArchiveIndexingText"/> dependency property.</summary>
    public static readonly DependencyProperty ArchiveIndexingTextProperty =
        DependencyProperty.Register(nameof(ArchiveIndexingText), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(""));

    /// <summary>Identifies the <see cref="IsArchiveLocked"/> dependency property.</summary>
    public static readonly DependencyProperty IsArchiveLockedProperty =
        DependencyProperty.Register(nameof(IsArchiveLocked), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="ChatZoomFactor"/> dependency property.</summary>
    public static readonly DependencyProperty ChatZoomFactorProperty =
        DependencyProperty.Register(nameof(ChatZoomFactor), typeof(double), typeof(ChatPanel),
            new PropertyMetadata(1.05, OnChatZoomFactorChanged));

    /// <summary>Identifies the <see cref="AllowAttachments"/> dependency property.</summary>
    public static readonly DependencyProperty AllowAttachmentsProperty =
        DependencyProperty.Register(nameof(AllowAttachments), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(true, OnAllowAttachmentsChanged));

    /// <summary>Identifies the <see cref="EmptyStateContent"/> dependency property.</summary>
    public static readonly DependencyProperty EmptyStateContentProperty =
        DependencyProperty.Register(nameof(EmptyStateContent), typeof(object), typeof(ChatPanel),
            new PropertyMetadata(null));

    #endregion

    #region Dependency Property Callbacks

    /// <summary>
    /// Called when the <see cref="Theme"/> property changes to apply the new theme.
    /// </summary>
    private static void OnThemePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized)
        {
            _ = panel.ApplyThemeAsync();
        }
    }

    /// <summary>
    /// Called when the <see cref="IsDebugMode"/> property changes to toggle debug UI in the renderer.
    /// </summary>
    private static void OnIsDebugModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized)
        {
            _ = panel._renderer.SetDebugModeAsync((bool)e.NewValue);
        }
    }

    /// <summary>
    /// Called when the <see cref="Title"/> property changes to update the title bar UI.
    /// </summary>
    private static void OnTitlePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
        {
            panel.UpdateTitleDisplay();
            panel.UpdateRefreshTooltip();
        }
    }

    /// <summary>
    /// Called when <see cref="AvailablePresets"/> changes to push preset list to the input area.
    /// </summary>
    private static void OnAvailablePresetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.AvailablePresets = e.NewValue as IList;
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedPreset"/> changes to sync the input area and update placeholder text.
    /// </summary>
    private static void OnSelectedPresetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatPanel panel) return;

        if (e.NewValue is ProviderPreset preset)
        {
            if (panel._inputArea is not null)
                panel._inputArea.SelectedPreset = preset;
            var label = string.IsNullOrEmpty(preset.ModelId)
                ? preset.Name
                : $"{preset.Name}/{preset.ModelId}";
            panel.UpdatePlaceholderWithProvider(label);
        }
        else
        {
            // Preset cleared (all providers removed)
            if (panel._inputArea is not null)
                panel._inputArea.SelectedPreset = null;
            panel.UpdatePlaceholderWithProvider(null);
        }
    }

    /// <summary>
    /// Called when <see cref="AvailableProfiles"/> changes to push prompt presets to the input area.
    /// </summary>
    private static void OnAvailableProfilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is IList<Profile> presets)
        {
            if (panel._inputArea is not null)
                panel._inputArea.AvailableProfiles = presets;
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedProfile"/> changes to sync the input area and update the system prompt.
    /// </summary>
    private static void OnSelectedProfileChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && e.NewValue is Profile preset)
        {
            if (panel._inputArea is not null)
            {
                panel._inputArea.SelectedProfile = preset;
                panel._inputArea.SelectProfileInCombo(preset);
            }
            // Update the actual system prompt used in requests
            panel.SystemPrompt = preset.SystemPrompt;
        }
    }

    /// <summary>
    /// Called when <see cref="RegisteredTools"/> changes to sync tools to the input area.
    /// </summary>
    private static void OnRegisteredToolsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.AvailableTools = panel.RegisteredTools;
        }
    }

    /// <summary>
    /// Called when <see cref="FontFamily"/> changes to update the chat rendering font.
    /// </summary>
    private static void OnFontFamilyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized && e.NewValue is string fontFamily)
        {
            _ = panel._renderer.SetFontFamilyAsync(fontFamily);
        }
    }

    /// <summary>
    /// Called when <see cref="FontSize"/> changes to update the chat rendering font size.
    /// </summary>
    private static void OnFontSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized && e.NewValue is double fontSize)
        {
            _ = panel._renderer.SetFontSizeAsync(fontSize);
        }
    }

    /// <summary>
    /// Called when <see cref="IsReadOnly"/> changes to show or hide the input area.
    /// </summary>
    private static void OnIsReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.Visibility = (bool)e.NewValue
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowTitleBar"/> changes to show or hide the title bar.
    /// </summary>
    private static void OnShowTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._titleBar is not null)
        {
            panel._titleBar.Visibility = (bool)e.NewValue
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Called when <see cref="WorkspaceFolders"/> changes to update the folder button badge.
    /// </summary>
    private static void OnWorkspaceFoldersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
        {
            panel.UpdateFolderButtonBadge();
        }
    }

    /// <summary>
    /// Called when <see cref="IsWorkspaceEnabled"/> changes to update the folder button appearance.
    /// </summary>
    private static void OnIsWorkspaceEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
        {
            panel.UpdateFolderButtonAppearance();
        }
    }

    /// <summary>
    /// Called when <see cref="AllowAttachments"/> changes to show or hide the attach button.
    /// </summary>
    private static void OnAllowAttachmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.ShowAttachButton = (bool)e.NewValue;
        }
    }

    #endregion

    #region Fields

    /// <summary>
    /// The WebView-based chat renderer responsible for HTML rendering of messages.
    /// </summary>
    private readonly WebViewChatRenderer _renderer = new();

    /// <summary>
    /// The in-memory collection of all chat messages in the current active path.
    /// </summary>
    private readonly ObservableCollection<ChatMessage> _messages = [];

    /// <summary>
    /// Full conversation tree: parentId → list of child messages.
    /// The <see cref="_messages"/> list remains the active path from root to current leaf.
    /// Root-level messages (no parent) use <see cref="TreeRootKey"/> as the key.
    /// </summary>
    private readonly Dictionary<string, List<ChatMessage>> _childrenMap = [];

    /// <summary>Sentinel key for root-level messages (ParentId == null).</summary>
    private const string TreeRootKey = "";

    /// <summary>
    /// Cancellation token source for the currently active streaming operation.
    /// </summary>
    private CancellationTokenSource? _streamingCts;

    /// <summary>
    /// Whether the WebView2 renderer has been initialized.
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// Whether restored messages have already been rendered (prevents duplicate rendering).
    /// </summary>
    private bool _hasRenderedRestored;

    /// <summary>
    /// Whether a conversation title has already been auto-generated.
    /// </summary>
    private bool _titleGenerated;

    #endregion

    #region Constants

    /// <summary>
    /// Background color matching chat.html --bg-primary for light theme (opaque, no alpha issues).
    /// </summary>
    private static readonly Windows.UI.Color LightBg = Windows.UI.Color.FromArgb(255, 0xF5, 0xF5, 0xF5);

    /// <summary>
    /// Background color matching chat.html --bg-primary for dark theme (opaque, no alpha issues).
    /// </summary>
    private static readonly Windows.UI.Color DarkBg = Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20);

    /// <summary>
    /// Maximum character length for tool results before truncation (~12k–15k tokens).
    /// </summary>
    private const int MaxToolResultChars = 50_000;

    /// <summary>
    /// Minimum length for a base64-like string to be considered binary content.
    /// </summary>
    private const int Base64DetectionThreshold = 10_000;

    #endregion

    #region Template Parts

    /// <summary>
    /// The root grid container of the control template.
    /// </summary>
    private Grid? _rootGrid;

    /// <summary>
    /// The panel shown when no messages are present (empty/welcome state).
    /// </summary>
    private Grid? _emptyStatePanel;

    /// <summary>
    /// The stack panel within the empty state that holds content and the input area.
    /// </summary>
    private StackPanel? _emptyStateContent;

    /// <summary>
    /// The input container control for message composition.
    /// </summary>
    private ComposeBar? _inputArea;

    /// <summary>
    /// The grid layout containing the WebView and input area during active chat.
    /// </summary>
    private Grid? _chatLayout;

    /// <summary>
    /// The title bar panel displaying the conversation title.
    /// </summary>
    private StackPanel? _titleBar;

    /// <summary>
    /// The text block displaying the conversation title text.
    /// </summary>
    private TextBlock? _titleText;

    /// <summary>
    /// The button that allows editing the conversation title.
    /// </summary>
    private Button? _titleEditButton;

    /// <summary>
    /// The button that triggers title regeneration.
    /// </summary>
    private Button? _titleRefreshButton;
    private Button? _titleFolderButton;

    // Folder flyout parts (resolved lazily on first Flyout.Opening)
    private Button? _folderAddButton;
    private TextBlock? _folderDisabledHint;
    private StackPanel? _folderList;
    private TextBlock? _folderEmpty;
    private TextBlock? _archiveDisabledHint;
    private ComboBox? _kbSelector;
    private TextBlock? _archiveEmpty;

    private bool _isConversationActive;
    private string? _greetingText;

    /// <summary>
    /// The WebView2 control used to render chat messages as HTML.
    /// </summary>
    private WebView2? _chatWebView;

    /// <summary>
    /// The panel shown in place of ComposeBar when a tool requires user confirmation.
    /// </summary>
    private ToolApprovalPanel? _approvalPanel;

    /// <summary>
    /// Completion source for awaiting user approval/rejection of a tool call.
    /// </summary>
    private TaskCompletionSource<(bool Approved, string? UserNote)>? _approvalTcs;

    private FrameworkElement? _searchBar;
    private TextBox? _searchTextBox;
    private TextBlock? _searchCount;
    private Button? _searchPrevButton;
    private Button? _searchNextButton;
    private Button? _searchCloseButton;
    private DispatcherTimer? _searchDebounceTimer;

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets or sets whether to automatically generate a title after the first assistant response.
    /// </summary>
    public bool AutoTitle
    {
        get => (bool)GetValue(AutoTitleProperty);
        set => SetValue(AutoTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to automatically summarize the conversation when input tokens exceed <see cref="MaxInputTokens"/>.
    /// </summary>
    public bool AutoSummarize
    {
        get => (bool)GetValue(AutoSummarizeProperty);
        set => SetValue(AutoSummarizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum input tokens before auto-summarization triggers. 0 = disabled (default).
    /// </summary>
    public int MaxInputTokens
    {
        get => (int)GetValue(MaxInputTokensProperty);
        set => SetValue(MaxInputTokensProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of consecutive tool call rounds before forcing a text response.
    /// </summary>
    public int MaxToolCallRounds
    {
        get => (int)GetValue(MaxToolCallRoundsProperty);
        set => SetValue(MaxToolCallRoundsProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of recent conversation turns to keep when summarizing.
    /// </summary>
    public int RecentTurnsToKeep
    {
        get => (int)GetValue(RecentTurnsToKeepProperty);
        set => SetValue(RecentTurnsToKeepProperty, value);
    }

    /// <summary>
    /// Gets or sets the provider used for utility tasks (title generation, summarization).
    /// Falls back to the main <see cref="Provider"/> if not set.
    /// </summary>
    public IAiProvider? UtilityProvider
    {
        get => (IAiProvider?)GetValue(UtilityProviderProperty);
        set => SetValue(UtilityProviderProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional workspace context provider. When set, the current workspace state
    /// is automatically injected into every AI request.
    /// </summary>
    public IWorkspaceContext? WorkspaceContext
    {
        get => (IWorkspaceContext?)GetValue(WorkspaceContextProperty);
        set => SetValue(WorkspaceContextProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional RAG context provider. When set, relevant context chunks are
    /// retrieved for the user's query and passed to the AI provider.
    /// </summary>
    public IContextProvider? ContextProvider
    {
        get => (IContextProvider?)GetValue(ContextProviderProperty);
        set => SetValue(ContextProviderProperty, value);
    }

    /// <summary>
    /// Gets or sets the registered tools available for AI tool calling. When non-empty, the provider uses
    /// CompleteAsync (non-streaming) to enable tool call responses.
    /// </summary>
    public IReadOnlyList<IAssistTool> RegisteredTools
    {
        get => (IReadOnlyList<IAssistTool>?)GetValue(RegisteredToolsProperty) ?? [];
        set => SetValue(RegisteredToolsProperty, value);
    }

    /// <summary>
    /// Gets or sets additional MCP tools that are executable but not sent in the API tools array.
    /// These tools are discovered via <c>search_tools</c> and made available to the <see cref="ToolCallExecutor"/>.
    /// </summary>
    public IReadOnlyList<IAssistTool> McpTools
    {
        get => (IReadOnlyList<IAssistTool>?)GetValue(McpToolsProperty) ?? [];
        set => SetValue(McpToolsProperty, value);
    }

    /// <summary>
    /// Optional delegate called before sending to auto-connect servers and filter tools by connection state.
    /// Receives user-selected tools, returns only tools that are actually usable.
    /// When set, <see cref="McpTools"/> should also be updated by the delegate to reflect connected servers.
    /// </summary>
    public Func<IReadOnlyList<IAssistTool>, Task<IReadOnlyList<IAssistTool>>>? PrepareToolsForSendAsync { get; set; }

    /// <summary>
    /// Validates whether a specialist name is registered and eligible for auto-approval.
    /// Injected by the host to connect ChatPanel (Controls) to SpecialistRegistry (App)
    /// without circular project references.
    /// </summary>
    public Func<string, bool>? IsRegisteredSpecialist { get; set; }

    /// <summary>
    /// Resolves a specialist name to its display name for UI labeling.
    /// Returns null if the specialist is not found.
    /// </summary>
    public Func<string, string?>? SpecialistDisplayNameResolver { get; set; }

    /// <summary>
    /// Gets or sets the font family name for chat message rendering.
    /// </summary>
    public new string? FontFamily
    {
        get => (string?)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the base font size in pixels for chat message rendering.
    /// </summary>
    public new double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the chat panel is in read-only mode (input area hidden).
    /// </summary>
    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the title bar is visible.
    /// </summary>
    public bool ShowTitleBar
    {
        get => (bool)GetValue(ShowTitleBarProperty);
        set => SetValue(ShowTitleBarProperty, value);
    }

    /// <summary>
    /// Gets or sets the workspace folder paths for the current conversation.
    /// When folders are present, the built-in Filesystem MCP server is activated.
    /// </summary>
    public IReadOnlyList<string>? WorkspaceFolders
    {
        get => (IReadOnlyList<string>?)GetValue(WorkspaceFoldersProperty);
        set => SetValue(WorkspaceFoldersProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Workspace capability is enabled in the current profile.
    /// When false, the folder flyout is read-only and grayed out.
    /// </summary>
    public bool IsWorkspaceEnabled
    {
        get => (bool)GetValue(IsWorkspaceEnabledProperty);
        set => SetValue(IsWorkspaceEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets the Knowledge Archive folder path for the current conversation.
    /// Single folder — each conversation has at most one archive folder.
    /// </summary>
    public string? KnowledgeArchiveFolder
    {
        get => (string?)GetValue(KnowledgeArchiveFolderProperty);
        set => SetValue(KnowledgeArchiveFolderProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Knowledge Archive capability is enabled in the current profile.
    /// </summary>
    public bool IsKnowledgeArchiveEnabled
    {
        get => (bool)GetValue(IsKnowledgeArchiveEnabledProperty);
        set => SetValue(IsKnowledgeArchiveEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Knowledge Archive is currently indexing.
    /// Controls visibility of the progress ring in the title bar and progress bar in the flyout.
    /// </summary>
    public bool IsArchiveIndexing
    {
        get => (bool)GetValue(IsArchiveIndexingProperty);
        set => SetValue(IsArchiveIndexingProperty, value);
    }

    /// <summary>
    /// Gets or sets the indexing progress as a percentage (0–100).
    /// </summary>
    public double ArchiveIndexingProgress
    {
        get => (double)GetValue(ArchiveIndexingProgressProperty);
        set => SetValue(ArchiveIndexingProgressProperty, value);
    }

    /// <summary>
    /// Gets or sets the indexing status text (e.g., "3/10 files...").
    /// </summary>
    public string ArchiveIndexingText
    {
        get => (string)GetValue(ArchiveIndexingTextProperty);
        set => SetValue(ArchiveIndexingTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Knowledge Archive folder is locked by another process.
    /// When true, shows a lock icon and hides the reindex button.
    /// </summary>
    public bool IsArchiveLocked
    {
        get => (bool)GetValue(IsArchiveLockedProperty);
        set => SetValue(IsArchiveLockedProperty, value);
    }

    /// <summary>
    /// Gets or sets the CSS zoom factor for the chat WebView2 content.
    /// Default is 1.05 (105%). Adjusts both zoom and max-width to keep visual width at 800px.
    /// </summary>
    public double ChatZoomFactor
    {
        get => (double)GetValue(ChatZoomFactorProperty);
        set => SetValue(ChatZoomFactorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether file attachments are allowed.
    /// </summary>
    public bool AllowAttachments
    {
        get => (bool)GetValue(AllowAttachmentsProperty);
        set => SetValue(AllowAttachmentsProperty, value);
    }

    /// <summary>
    /// Gets or sets custom content displayed in the empty state panel.
    /// </summary>
    public object? EmptyStateContent
    {
        get => GetValue(EmptyStateContentProperty);
        set => SetValue(EmptyStateContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the AI provider used for streaming chat responses.
    /// </summary>
    public IAiProvider? Provider
    {
        get => (IAiProvider?)GetValue(ProviderProperty);
        set => SetValue(ProviderProperty, value);
    }

    /// <summary>
    /// Gets or sets the placeholder text shown in the input area.
    /// </summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>
    /// Gets or sets the system prompt included with every AI request.
    /// </summary>
    public string? SystemPrompt
    {
        get => (string?)GetValue(SystemPromptProperty);
        set => SetValue(SystemPromptProperty, value);
    }

    /// <summary>
    /// Gets or sets the persistent memory text injected into the system prompt.
    /// </summary>
    public string? MemoryText
    {
        get => (string?)GetValue(MemoryTextProperty);
        set => SetValue(MemoryTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the theme mode for the chat panel (System, Light, or Dark).
    /// </summary>
    public ChatTheme Theme
    {
        get => (ChatTheme)GetValue(ThemeProperty);
        set => SetValue(ThemeProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available provider presets shown in the input area dropdown.
    /// </summary>
    public IList? AvailablePresets
    {
        get => (IList?)GetValue(AvailablePresetsProperty);
        set => SetValue(AvailablePresetsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected provider preset.
    /// </summary>
    public ProviderPreset? SelectedPreset
    {
        get => (ProviderPreset?)GetValue(SelectedPresetProperty);
        set => SetValue(SelectedPresetProperty, value);
    }

    /// <summary>
    /// Gets or sets the list of available prompt presets shown in the input area dropdown.
    /// </summary>
    public IList<Profile>? AvailableProfiles
    {
        get => (IList<Profile>?)GetValue(AvailableProfilesProperty);
        set => SetValue(AvailableProfilesProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected prompt preset.
    /// </summary>
    public Profile? SelectedProfile
    {
        get => (Profile?)GetValue(SelectedProfileProperty);
        set => SetValue(SelectedProfileProperty, value);
    }

    /// <summary>
    /// Enables debug mode: adds "Copy Request" / "Copy Response" buttons to the last
    /// message pair, allowing inspection of the actual API request body and raw response.
    /// </summary>
    public bool IsDebugMode
    {
        get => (bool)GetValue(IsDebugModeProperty);
        set => SetValue(IsDebugModeProperty, value);
    }

    /// <summary>
    /// Gets or sets the conversation title displayed in the title bar.
    /// </summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the user selects a different provider preset.
    /// </summary>
    public event EventHandler<ProviderPreset>? PresetChanged;

    /// <summary>
    /// Occurs when a new message (user or assistant) is added to the conversation.
    /// </summary>
    public event EventHandler<ChatMessage>? MessageAdded;

    /// <summary>
    /// Occurs when the user switches to a different conversation branch.
    /// </summary>
    public event EventHandler? BranchChanged;

    /// <summary>
    /// Occurs when a conversation title is generated or regenerated by the AI provider.
    /// </summary>
    public event EventHandler<string>? TitleGenerated;

    /// <summary>
    /// Occurs when the user selects a different profile.
    /// </summary>
    public event EventHandler<Profile>? ProfileChanged;

    /// <summary>
    /// Occurs when the user clicks the title edit button.
    /// </summary>
    public event EventHandler<string>? TitleEditRequested;

    /// <summary>
    /// Occurs when the user adds or removes workspace folders via the title bar flyout.
    /// The event argument contains the updated folder list.
    /// </summary>
    public event EventHandler<IReadOnlyList<string>>? WorkspaceFoldersChanged;

    /// <summary>
    /// Occurs when the user clicks "Add Folder" in the workspace folders flyout.
    /// The App layer should handle this to show a FolderPicker and update <see cref="WorkspaceFolders"/>.
    /// </summary>
    public event EventHandler? WorkspaceFolderAddRequested;

    /// <summary>
    /// Occurs when the user sets or removes the Knowledge Archive folder via the flyout.
    /// The event argument is the folder path (null to remove).
    /// </summary>
    public event EventHandler<string?>? KnowledgeArchiveFolderChanged;

    /// <summary>
    /// Callback that returns the list of available knowledge bases for the KB selector.
    /// Set by the App layer (e.g., ChatTabView) since Controls cannot reference App services.
    /// </summary>
    public Func<List<KbItem>>? KbItemsProvider { get; set; }

    /// <summary>
    /// Occurs when a keyboard shortcut is pressed inside the WebView2 that should be handled by the host.
    /// </summary>
    public event EventHandler<string>? KeyboardShortcutPressed;

    /// <summary>
    /// Occurs when the control wants to display a notification (e.g., image saved/copied).
    /// </summary>
    public event EventHandler<(string Title, string Message)>? NotificationRequested;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ChatPanel"/> class.
    /// </summary>
    public ChatPanel()
    {
        DefaultStyleKey = typeof(ChatPanel);
        _renderer.ContinueRequested += OnContinueRequested;
        _renderer.MessageCopyRequested += OnMessageCopyRequested;
        _renderer.RetryRequested += OnRetryRequested;
        _renderer.EditRequested += OnEditRequested;
        _renderer.BranchSwitchRequested += OnBranchSwitchRequested;
        _renderer.SummarizeRequested += OnSummarizeRequested;
        _renderer.ImageSaveRequested += OnImageSaveRequested;
        _renderer.ImageCopyRequested += OnImageCopyRequested;
        _renderer.KeyboardShortcutPressed += (_, shortcut) =>
        {
            if (shortcut == "Ctrl+F")
            {
                ToggleSearchBar();
                return;
            }
            KeyboardShortcutPressed?.Invoke(this, shortcut);
        };
    }

    #endregion

    #region Overrides

    /// <inheritdoc />
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        // Detach old event handlers
        if (_inputArea is not null)
        {
            _inputArea.MessageSent -= OnMessageSent;
            _inputArea.PresetChanged -= OnInputPresetChanged;
            _inputArea.ProfileChanged -= OnInputProfileChanged;
            _inputArea.StopRequested -= OnStopRequested;
            _inputArea.SummarizeRequested -= OnInputSummarizeRequested;
        }
        if (_approvalPanel is not null)
        {
            _approvalPanel.Approved -= OnToolApproved;
            _approvalPanel.Rejected -= OnToolRejected;
        }
        if (_rootGrid is not null)
        {
            _rootGrid.DragOver -= OnDragOver;
            _rootGrid.Drop -= OnDrop;
        }
        if (_titleEditButton is not null)
            _titleEditButton.Click -= OnTitleEditClick;
        if (_titleRefreshButton is not null)
            _titleRefreshButton.Click -= OnTitleRefreshClick;
        // Reset folder flyout part references (will be re-resolved on next Flyout.Opening)
        _folderAddButton = null;
        _folderDisabledHint = null;
        _folderList = null;
        _folderEmpty = null;
        _archiveDisabledHint = null;
        _kbSelector = null;
        _archiveEmpty = null;

        // Get template parts
        _rootGrid = GetTemplateChild("PART_RootGrid") as Grid;
        _emptyStatePanel = GetTemplateChild("PART_EmptyStatePanel") as Grid;
        _emptyStateContent = GetTemplateChild("PART_EmptyStateContent") as StackPanel;
        _inputArea = GetTemplateChild("PART_InputArea") as ComposeBar;
        _chatLayout = GetTemplateChild("PART_ChatLayout") as Grid;
        _titleBar = GetTemplateChild("PART_TitleBar") as StackPanel;
        _titleText = GetTemplateChild("PART_TitleText") as TextBlock;
        _titleEditButton = GetTemplateChild("PART_TitleEditButton") as Button;
        _titleRefreshButton = GetTemplateChild("PART_TitleRefreshButton") as Button;
        _titleFolderButton = GetTemplateChild("PART_TitleFolderButton") as Button;

        _chatWebView = GetTemplateChild("PART_ChatWebView") as WebView2;
        _approvalPanel = GetTemplateChild("PART_ToolApprovalPanel") as ToolApprovalPanel;

        // Wire search bar
        _searchBar = GetTemplateChild("PART_SearchBar") as FrameworkElement;
        _searchTextBox = GetTemplateChild("PART_SearchTextBox") as TextBox;
        _searchCount = GetTemplateChild("PART_SearchCount") as TextBlock;
        _searchPrevButton = GetTemplateChild("PART_SearchPrevButton") as Button;
        _searchNextButton = GetTemplateChild("PART_SearchNextButton") as Button;
        _searchCloseButton = GetTemplateChild("PART_SearchCloseButton") as Button;

        if (_searchTextBox is not null)
        {
            _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _searchDebounceTimer.Tick += async (s, e) =>
            {
                _searchDebounceTimer.Stop();
                await ExecuteSearchAsync(_searchTextBox.Text);
            };
            _searchTextBox.TextChanged += (s, e) =>
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };
            _searchTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    CloseSearchBar();
                    e.Handled = true;
                }
                else if (e.Key == Windows.System.VirtualKey.Enter)
                {
                    _ = NavigateSearchAsync(1);
                    e.Handled = true;
                }
            };
        }
        if (_searchPrevButton is not null) _searchPrevButton.Click += (s, e) => _ = NavigateSearchAsync(-1);
        if (_searchNextButton is not null) _searchNextButton.Click += (s, e) => _ = NavigateSearchAsync(1);
        if (_searchCloseButton is not null) _searchCloseButton.Click += (s, e) => CloseSearchBar();

        // Wire approval panel events
        if (_approvalPanel is not null)
        {
            _approvalPanel.Approved += OnToolApproved;
            _approvalPanel.Rejected += OnToolRejected;
        }

        // Set initial background to match CSS --bg-primary (before WebView2 loads)
        if (_rootGrid is not null)
            _rootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(LightBg);

        // Push title text (may have been set before template was applied)
        if (_titleText is not null && !string.IsNullOrEmpty(Title))
            _titleText.Text = Title;

        // Attach event handlers and sync current property values
        if (_inputArea is not null)
        {
            _inputArea.InputFocused += async (_, _) =>
            {
                if (_isInitialized) await _renderer.CloseImageModalAsync();
            };
            _inputArea.MessageSent += OnMessageSent;
            _inputArea.PresetChanged += OnInputPresetChanged;
            _inputArea.ProfileChanged += OnInputProfileChanged;
            _inputArea.StopRequested += OnStopRequested;
            _inputArea.SummarizeRequested += OnInputSummarizeRequested;
            // Push current values (may have been set before template was applied)
            if (AvailablePresets is { } presets)
                _inputArea.AvailablePresets = presets;
            if (SelectedPreset is { } selectedPreset)
                _inputArea.SelectedPreset = selectedPreset;
            if (AvailableProfiles is { } promptPresets)
                _inputArea.AvailableProfiles = promptPresets;
            if (SelectedProfile is { } selectedProfile)
            {
                _inputArea.SelectedProfile = selectedProfile;
                _inputArea.SelectProfileInCombo(selectedProfile);
            }

            // Sync tools and visibility settings
            _inputArea.AvailableTools = RegisteredTools;
            _inputArea.ShowAttachButton = AllowAttachments;
            if (IsReadOnly)
                _inputArea.Visibility = Visibility.Collapsed;
        }
        if (_rootGrid is not null)
        {
            _rootGrid.DragOver += OnDragOver;
            _rootGrid.Drop += OnDrop;
        }
        if (_titleEditButton is not null)
        {
            _titleEditButton.Click += OnTitleEditClick;
            var tooltip = Res.GetString("ChatPanel_EditTitleTooltip");
            SetBottomRightToolTip(_titleEditButton, !string.IsNullOrEmpty(tooltip) ? tooltip : "Edit title");
        }
        if (_titleRefreshButton is not null)
            _titleRefreshButton.Click += OnTitleRefreshClick;
        if (_titleFolderButton is not null)
        {
            // Wire Flyout.Opening for lazy PART_ resolution and content population
            if (_titleFolderButton.Flyout is Flyout folderFlyout)
            {
                folderFlyout.Opened += OnFolderFlyoutOpened;
                folderFlyout.Opening += OnFolderFlyoutOpening;
            }

            SetBottomRightToolTip(_titleFolderButton, Res.GetString("Folder_Tooltip") ?? "Folders");
        }

        UpdateFolderButtonBadge();
        UpdateFolderButtonAppearance();
        UpdateTitleDisplay();

        // Subscribe to Loaded for WebView2 initialization
        Loaded += OnLoaded;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns a read-only snapshot of all messages in the conversation.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetMessages() => _messages;

    /// <summary>
    /// Returns all messages in the conversation tree (active path + all branches).
    /// Used for saving the full tree to disk.
    /// </summary>
    public IReadOnlyList<ChatMessage> GetAllMessages()
    {
        if (_childrenMap.Count == 0) return _messages;
        return [.. _childrenMap.Values.SelectMany(v => v).Distinct()];
    }

    /// <summary>
    /// Registers a message in the tree without adding to the active path.
    /// Used for loading inactive branch messages from saved conversations.
    /// </summary>
    public void RegisterBranchMessage(ChatMessage msg)
    {
        RegisterInTree(msg, updateActiveChild: false);
    }

    /// <summary>
    /// Adds a previously saved message to the conversation (for restoring saved conversations).
    /// Messages added before the WebView is initialized will be rendered once initialization completes.
    /// </summary>
    public void AddRestoredMessage(ChatRole role, string content,
        string? providerName = null, string? providerModelId = null,
        string? id = null, string? parentId = null,
        IReadOnlyList<ToolCall>? toolCalls = null, string? toolCallId = null,
        string? activeChildId = null)
    {
        var msg = id is not null
            ? new ChatMessage(id, role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId }
            : new ChatMessage(role, content) { ProviderName = providerName, ProviderModelId = providerModelId, ParentId = parentId, ToolCalls = toolCalls, ToolCallId = toolCallId, ActiveChildId = activeChildId };
        RegisterInTree(msg);
        _messages.Add(msg);
    }

    /// <summary>
    /// Manually trigger conversation summarization.
    /// Compresses older messages into a system summary, keeping the most recent turns.
    /// </summary>
    public async Task SummarizeConversationAsync()
    {
        if (Provider is null || _messages.Count <= RecentTurnsToKeep) return;
        await SummarizeHistoryAsync(CancellationToken.None);
    }

    /// <summary>
    /// Sets keyboard focus to the message input text box.
    /// </summary>
    public void FocusInput() => _inputArea?.FocusInput();

    /// <summary>
    /// Clears all messages and resets the chat panel to its empty state.
    /// </summary>
    public async void ClearConversation()
    {
        _streamingCts?.Cancel();
        _messages.Clear();
        if (_isInitialized && _chatWebView is not null)
        {
            await _renderer.SetThemeAsync(IsDarkTheme());
            await _renderer.InitializeAsync(_chatWebView);
            await ApplyThemeAsync();
        }

        // Switch back to empty state
        if (_chatLayout?.Visibility == Microsoft.UI.Xaml.Visibility.Visible)
        {
            _chatLayout.Children.Remove(_inputArea);
            if (_inputArea is not null)
                _inputArea.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
            if (_inputArea is not null)
                _emptyStateContent?.Children.Add(_inputArea);
            _chatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            if (_emptyStatePanel is not null)
                _emptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
        }
        UpdateSummarizeButtonState();
    }

    /// <summary>
    /// Regenerates the conversation title from the full message history.
    /// </summary>
    public async Task RegenerateTitleAsync()
    {
        await GenerateTitleCoreAsync();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the Loaded event to initialize the WebView2 renderer and render any pre-existing messages.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized) return;

        try
        {
            if (_chatWebView is null) return;

            await _renderer.InitializeAsync(_chatWebView);
            _isInitialized = true;
            await ApplyThemeAsync();
            await ApplyLocaleStringsAsync();
            ApplyChatZoom();
            if (IsDebugMode)
                await _renderer.SetDebugModeAsync(true);

            // Render any pre-existing messages (restored conversations)
            await RenderRestoredMessagesAsync();

            // Warm up the WebView2 internal HWND so accelerator keys
            // and focus work immediately (without waiting for user click).
            _chatWebView.Focus(FocusState.Programmatic);
            _inputArea?.FocusInput();

            // Listen for theme changes
            ActualThemeChanged += async (_, _) => await ApplyThemeAsync();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
        }
    }

    /// <summary>
    /// Handles the MessageSent event from the input area to send a user message and stream an assistant response.
    /// </summary>
    private async void OnMessageSent(object? sender, MessageSentEventArgs e)
    {
        if (!_isInitialized) return;
        if (string.IsNullOrWhiteSpace(e.Text) && e.Attachments.Count == 0) return;

        SwitchToChatLayout();

        // Add user message with attachments
        var parentId = _messages.Count > 0 ? _messages[^1].Id : null;
        var userMessage = new ChatMessage(ChatRole.User, e.Text) { Attachments = e.Attachments, ParentId = parentId };
        RegisterInTree(userMessage);
        _messages.Add(userMessage);
        await _renderer.AppendUserMessageAsync(
            userMessage.Id, userMessage.Content, userMessage.Timestamp.ToString("O"),
            userMessage.Attachments, userMessage.SiblingIndex, userMessage.SiblingCount);
        MessageAdded?.Invoke(this, userMessage);
        DiagnosticLogger.LogInfo($"[Chat] User message sent, attachments={e.Attachments.Count}");

        // Stream assistant response
        if (Provider is null)
        {
            DiagnosticLogger.LogWarning("[Chat] Provider is null when sending message");
            var errorMsg = new ChatMessage(ChatRole.Assistant) { Content = "[Error: No AI provider configured]", ParentId = userMessage.Id };
            RegisterInTree(errorMsg);
            _messages.Add(errorMsg);
            await _renderer.BeginAssistantMessageAsync(errorMsg.Id, "Error", null);
            await _renderer.FinalizeMessageAsync(errorMsg.Id, errorMsg.Content);
            return;
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId,
            ParentId = userMessage.Id
        };
        RegisterInTree(assistantMessage);
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id, Provider.ProviderName, Provider.ModelId);
        MessageAdded?.Invoke(this, assistantMessage);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            {
                var result = await StreamAndExecuteAsync(assistantMessage, ct);
                await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content,
                    result.IsTruncated, result.Usage?.TotalTokens ?? 0);
                DiagnosticLogger.LogInfo($"[Chat] Response complete — tokens={result.Usage?.TotalTokens ?? 0}, truncated={result.IsTruncated}");
            }

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(userMessage.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);

            TryGenerateTitleAsync();

            // Auto-summarize if enabled and token threshold exceeded
            if (AutoSummarize && MaxInputTokens > 0 && Provider.LastUsage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await SummarizeHistoryAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogInfo("[Chat] Streaming cancelled by user");
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
            UpdateSummarizeButtonState();
        }
    }

    /// <summary>
    /// Handles the message copy request from the renderer by copying message content to the clipboard.
    /// </summary>
    private void OnMessageCopyRequested(object? sender, string messageId)
    {
        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null || string.IsNullOrEmpty(message.Content)) return;

        var dp = new DataPackage();
        dp.SetText(message.Content);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
    }

    /// <summary>
    /// Handles the continue request from the renderer to resume streaming an assistant message.
    /// </summary>
    private async void OnContinueRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;
        DiagnosticLogger.LogInfo($"[Chat] Continue requested for message {messageId}");

        // Find the assistant message to continue
        var assistantMessage = _messages.LastOrDefault(m =>
            m.Role == ChatRole.Assistant && m.Id == messageId);
        if (assistantMessage is null) return;

        // Add a user message asking to continue (not shown in UI)
        var continueMessage = new ChatMessage(ChatRole.User, "Continue writing from where you left off.")
        {
            ParentId = assistantMessage.Id
        };
        RegisterInTree(continueMessage);
        _messages.Add(continueMessage);

        // Resume the existing assistant message bubble for continued streaming
        assistantMessage.IsStreaming = true;
        await _renderer.ResumeMessageAsync(assistantMessage.Id, assistantMessage.Content);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var request = await CreateRequestAsync([.. _messages]);

            var priorLength = assistantMessage.Content.Length;
            var result = await ConsumeStreamAsync(Provider.StreamAsync(request, ct), assistantMessage, ct);

            // Remove the continue user message from history -- replace with combined assistant content
            _messages.Remove(continueMessage);

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, result.IsTruncated, result.Usage?.TotalTokens ?? 0);

            if (IsDebugMode)
            {
                // Find the original user message for this assistant response
                var idx2 = _messages.IndexOf(assistantMessage);
                var origUserMsg = idx2 > 0 ? _messages[idx2 - 1] : null;
                if (origUserMsg is not null)
                    await _renderer.SetDebugDataAsync(origUserMsg.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);
            }
        }
        catch (OperationCanceledException)
        {
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
            UpdateSummarizeButtonState();
        }
    }

    /// <summary>
    /// Handles the retry request from the renderer to re-send a user message and get a new response.
    /// </summary>
    private async void OnRetryRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;

        var userMessage = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Id == messageId);
        if (userMessage is null) return;

        // Find the index of this user message and remove everything after it
        var idx = _messages.IndexOf(userMessage);
        if (idx < 0) return;
        while (_messages.Count > idx + 1)
        {
            _messages.RemoveAt(_messages.Count - 1);
        }
        await _renderer.RemoveMessagesAfterAsync(messageId);

        // Re-send with the same text
        await StreamAssistantResponseAsync(userMessage);
    }

    /// <summary>
    /// Handles the edit request from the renderer to update a user message and re-stream a response.
    /// </summary>
    private async void OnEditRequested(object? sender, (string MessageId, string NewText) e)
    {
        if (!_isInitialized || Provider is null) return;

        var original = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Id == e.MessageId);
        if (original is null) return;

        // Create sibling message (same ParentId as original → branching)
        var edited = new ChatMessage(ChatRole.User, e.NewText)
        {
            ParentId = original.ParentId,
            Attachments = original.Attachments,
        };
        RegisterInTree(edited);

        // Switch active path: remove from original's position onward
        var idx = _messages.IndexOf(original);
        if (idx < 0) return;
        while (_messages.Count > idx)
            _messages.RemoveAt(_messages.Count - 1);
        _messages.Add(edited);

        // Update renderer: remove old messages, render new branch with navigator
        var removeAfterId = idx > 0 ? _messages[idx - 1].Id : null;
        if (removeAfterId is not null)
            await _renderer.RemoveMessagesAfterAsync(removeAfterId);
        else
            await _renderer.ClearMessagesAsync();

        await _renderer.AppendUserMessageAsync(
            edited.Id, edited.Content, edited.Timestamp.ToString("O"),
            edited.Attachments, edited.SiblingIndex, edited.SiblingCount);

        // Also update the branch nav on the original message's siblings (if they're visible in other views)
        // Not needed here since we re-rendered from the branch point

        // Stream new response
        await StreamAssistantResponseAsync(edited);
    }

    /// <summary>
    /// Handles branch switch requests from the chat UI.
    /// Rebuilds the active path from root to the selected branch's leaf.
    /// </summary>
    private async void OnBranchSwitchRequested(object? sender, (string MessageId, int Direction) e)
    {
        var current = _messages.FirstOrDefault(m => m.Id == e.MessageId);
        if (current is null) return;

        // Find sibling in the tree
        if (!_childrenMap.TryGetValue(current.ParentId ?? TreeRootKey, out var siblings)) return;
        var newIndex = current.SiblingIndex + e.Direction;
        if (newIndex < 0 || newIndex >= siblings.Count) return;
        var target = siblings[newIndex];

        // Update parent's ActiveChildId to track the user's branch selection
        var parent = _messages.FirstOrDefault(m => m.Id == current.ParentId);
        if (parent is not null) parent.ActiveChildId = target.Id;
        BranchChanged?.Invoke(this, EventArgs.Empty);

        // Truncate active path from current message's position
        var idx = _messages.IndexOf(current);
        if (idx < 0) return;
        while (_messages.Count > idx)
            _messages.RemoveAt(_messages.Count - 1);

        // Walk from target down to its deepest leaf (following last child at each level)
        var path = BuildPathToLeaf(target);
        foreach (var msg in path)
            _messages.Add(msg);

        // Re-render from the branch point
        var removeAfterId = idx > 0 ? _messages[idx - 1].Id : null;
        if (removeAfterId is not null)
            await _renderer.RemoveMessagesAfterAsync(removeAfterId);
        else
            await _renderer.ClearMessagesAsync();

        foreach (var msg in path)
        {
            if (msg.Role == ChatRole.User)
            {
                await _renderer.AppendUserMessageAsync(
                    msg.Id, msg.Content, msg.Timestamp.ToString("O"),
                    msg.Attachments, msg.SiblingIndex, msg.SiblingCount);
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                await _renderer.BeginAssistantMessageAsync(msg.Id, msg.ProviderName, msg.ProviderModelId);
                await _renderer.FinalizeMessageAsync(msg.Id, msg.Content);
            }
        }
    }

    /// <summary>
    /// Handles the summarize request from the renderer to summarize a single message via AI.
    /// </summary>
    private async void OnSummarizeRequested(object? sender, string messageId)
    {
        if (!_isInitialized || Provider is null) return;

        var message = _messages.FirstOrDefault(m => m.Id == messageId);
        if (message is null || string.IsNullOrEmpty(message.Content)) return;

        // Create a summarization request for this single message
        var summaryRequest = new AiRequest
        {
            Messages =
            [
                new ChatMessage(ChatRole.User,
                    "Summarize the following text concisely:\n\n" + message.Content)
            ],
            SystemPrompt = "You are a helpful assistant. Provide a concise summary."
        };

        var summaryMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId
        };
        _messages.Add(summaryMessage);
        await _renderer.BeginAssistantMessageAsync(summaryMessage.Id, Provider.ProviderName, Provider.ModelId);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var result = await ConsumeStreamAsync(Provider.StreamAsync(summaryRequest, ct), summaryMessage, ct);
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content, result.IsTruncated);
        }
        catch (OperationCanceledException)
        {
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            summaryMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content);
        }
        finally
        {
            summaryMessage.IsStreaming = false;
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
            UpdateSummarizeButtonState();
        }
    }

    /// <summary>
    /// Handles the image save request by presenting a FileSavePicker and writing bytes to the chosen file.
    /// </summary>
    private async void OnImageSaveRequested(object? sender, string source)
    {
        try
        {
            byte[] bytes;
            string ext;

            if (source.StartsWith("data:"))
            {
                var commaIdx = source.IndexOf(',');
                var header = source[..commaIdx];
                ext = header.Contains("image/jpeg") ? ".jpg"
                    : header.Contains("image/gif") ? ".gif"
                    : header.Contains("image/webp") ? ".webp"
                    : ".png";
                bytes = Convert.FromBase64String(source[(commaIdx + 1)..]);
            }
            else
            {
                using var http = new HttpClient();
                bytes = await http.GetByteArrayAsync(source);
                ext = source.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
                   || source.Contains(".jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
            }

            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.Downloads;
            picker.SuggestedFileName = $"image_{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";
            picker.FileTypeChoices.Add("Image", new[] { ext });

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(GetWindow());
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            // Atomic write: defer → write → complete to prevent partial files
            Windows.Storage.CachedFileManager.DeferUpdates(file);
            await Windows.Storage.FileIO.WriteBytesAsync(file, bytes);
            var status = await Windows.Storage.CachedFileManager.CompleteUpdatesAsync(file);

            if (status != Windows.Storage.Provider.FileUpdateStatus.Complete)
            {
                DiagnosticLogger.LogWarning($"[Image] Save not completed: {status}");
                return;
            }

            NotificationRequested?.Invoke(this, (
                Res.GetString("Chat_ImageSaved") ?? "Image saved",
                file.Name));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Image] Save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the image copy request by decoding/downloading the image and placing it on the clipboard.
    /// </summary>
    private async void OnImageCopyRequested(object? sender, string source)
    {
        try
        {
            byte[] bytes;

            if (source.StartsWith("data:"))
            {
                var commaIdx = source.IndexOf(',');
                bytes = Convert.FromBase64String(source[(commaIdx + 1)..]);
            }
            else
            {
                using var http = new HttpClient();
                bytes = await http.GetByteArrayAsync(source);
            }

            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var dp = new DataPackage();
            dp.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(dp);
            Clipboard.Flush();

            NotificationRequested?.Invoke(this, (
                Res.GetString("Chat_ImageCopied") ?? "Copied to clipboard",
                ""));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning($"[Image] Copy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves the parent <see cref="Window"/> for the current XamlRoot, used for native picker interop.
    /// </summary>
    private Window GetWindow()
    {
        if (XamlRoot?.Content is FrameworkElement fe)
        {
            if (fe.XamlRoot is not null)
            {
                foreach (var window in WindowHelper.ActiveWindows)
                {
                    if (window.Content?.XamlRoot == fe.XamlRoot)
                        return window;
                }
            }
        }
        throw new InvalidOperationException("Unable to find the parent Window for FileSavePicker.");
    }

    /// <summary>
    /// Handles the preset changed event from the input area to propagate the selection.
    /// </summary>
    private void OnInputPresetChanged(object? sender, ProviderPreset preset)
    {
        SelectedPreset = preset;
        PresetChanged?.Invoke(this, preset);
    }

    /// <summary>
    /// Handles the stop button click from the input area to cancel the current streaming operation.
    /// </summary>
    private void OnStopRequested(object? sender, EventArgs e)
    {
        _streamingCts?.Cancel();
    }

    /// <summary>
    /// Handles the summarize button click from the input area to trigger conversation summarization.
    /// Shows the summary as a streamed assistant message and replaces older messages internally.
    /// </summary>
    private async void OnInputSummarizeRequested(object? sender, EventArgs e)
    {
        if (Provider is null || _messages.Count <= RecentTurnsToKeep) return;

        var turnsToKeep = Math.Min(RecentTurnsToKeep, _messages.Count);
        var oldMessages = _messages.Take(_messages.Count - turnsToKeep).ToList();
        if (oldMessages.Count == 0) return;

        var historyText = string.Join("\n",
            oldMessages.Select(m => $"{m.Role}: {m.Content}"));

        var summaryRequest = new AiRequest
        {
            Messages =
            [
                new ChatMessage(ChatRole.User,
                    "Summarize the following conversation concisely, preserving key context and decisions:\n\n" +
                    historyText)
            ],
            SystemPrompt = "You are a helpful assistant that creates concise conversation summaries."
        };

        var summaryMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId
        };
        _messages.Add(summaryMessage);
        await _renderer.BeginAssistantMessageAsync(summaryMessage.Id, Provider.ProviderName, Provider.ModelId);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var result = await ConsumeStreamAsync(Provider.StreamAsync(summaryRequest, ct), summaryMessage, ct);
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content, result.IsTruncated);

            // Replace old messages with a summary system message (internal only)
            for (var i = 0; i < oldMessages.Count; i++)
                _messages.RemoveAt(0);
            _messages.Insert(0, new ChatMessage(ChatRole.System,
                $"[Previous conversation summary]\n{summaryMessage.Content}"));
        }
        catch (OperationCanceledException)
        {
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            summaryMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(summaryMessage.Id, summaryMessage.Content);
        }
        finally
        {
            summaryMessage.IsStreaming = false;
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
            UpdateSummarizeButtonState();
        }
    }

    /// <summary>
    /// Handles the prompt preset changed event from the input area to update the system prompt.
    /// </summary>
    private void OnInputProfileChanged(object? sender, Profile profile)
    {
        SelectedProfile = profile;
        SystemPrompt = profile.SystemPrompt;
        ProfileChanged?.Invoke(this, profile);
    }

    /// <summary>
    /// Handles the title edit button click to raise the <see cref="TitleEditRequested"/> event.
    /// </summary>
    private void OnTitleEditClick(object sender, RoutedEventArgs e)
    {
        TitleEditRequested?.Invoke(this, Title ?? "");
    }

    /// <summary>
    /// Handles the title refresh button click to regenerate the conversation title.
    /// </summary>
    private async void OnTitleRefreshClick(object sender, RoutedEventArgs e)
    {
        await RegenerateTitleAsync();
    }

    /// <summary>
    /// Handles the folder flyout Opened event (visual tree is ready).
    /// Resolves PART_ elements and wires click handlers on first open.
    /// </summary>
    private void OnFolderFlyoutOpened(object? sender, object e)
    {
        if (sender is not Flyout flyout) return;

        // Resolve PART_ elements on first open (VisualTree available only after Opened)
        if (_folderAddButton is null && flyout.Content is FrameworkElement root)
        {
            _folderAddButton = FindDescendantByName<Button>(root, "PART_FolderAddButton");
            _folderDisabledHint = FindDescendantByName<TextBlock>(root, "PART_FolderDisabledHint");
            _folderList = FindDescendantByName<StackPanel>(root, "PART_FolderList");
            _folderEmpty = FindDescendantByName<TextBlock>(root, "PART_FolderEmpty");
            _archiveDisabledHint = FindDescendantByName<TextBlock>(root, "PART_ArchiveDisabledHint");
            _kbSelector = FindDescendantByName<ComboBox>(root, "PART_KbSelector");
            _archiveEmpty = FindDescendantByName<TextBlock>(root, "PART_ArchiveEmpty");

            // Localize flyout text (x:Uid doesn't work in ControlTemplate)
            LocalizeFlyoutText(root);

            // Wire click handlers (once)
            if (_folderAddButton is not null)
                _folderAddButton.Click += (s, e2) => WorkspaceFolderAddRequested?.Invoke(this, EventArgs.Empty);
            if (_kbSelector is not null)
            {
                _kbSelector.SelectionChanged += (s, e2) =>
                {
                    if (_kbSelector.SelectedItem is KbItem selected)
                    {
                        KnowledgeArchiveFolder = selected.Id;
                        KnowledgeArchiveFolderChanged?.Invoke(this, selected.Id);
                    }
                    else
                    {
                        KnowledgeArchiveFolder = null;
                        KnowledgeArchiveFolderChanged?.Invoke(this, null);
                    }
                };
            }

            // Populate now (Opening couldn't do it because parts weren't resolved yet)
            PopulateFolderFlyout();
        }
    }

    /// <summary>
    /// Handles the folder flyout Opening event.
    /// Populates dynamic content if PART_ elements are already resolved.
    /// </summary>
    private void OnFolderFlyoutOpening(object? sender, object e)
    {
        // Only populate if parts are already resolved (after first Opened)
        if (_folderList is not null)
            PopulateFolderFlyout();
    }

    /// <summary>
    /// Updates the folder flyout content based on current workspace folders and knowledge archive state.
    /// Called on every Flyout.Opening event.
    /// </summary>
    private void PopulateFolderFlyout()
    {
        if (_folderList is null) return;

        var folders = WorkspaceFolders?.ToList() ?? [];
        var isEnabled = IsWorkspaceEnabled;

        // Workspace section visibility
        if (_folderDisabledHint is not null)
            _folderDisabledHint.Visibility = Visibility.Visible;
        if (_folderEmpty is not null)
            _folderEmpty.Visibility = folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // Rebuild folder list items
        _folderList.Children.Clear();

        var removeTooltipText = Res.GetString("FolderFlyout_RemoveTooltip") ?? "Remove";

        foreach (var folder in folders)
        {
            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
            };

            var folderText = new TextBlock
            {
                Text = folder,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
                Margin = new Thickness(8, 0, 0, 0),
                Opacity = isEnabled ? 1.0 : 0.5,
            };
            Grid.SetColumn(folderText, 0);
            row.Children.Add(folderText);

            var capturedFolder = folder;
            var removeButton = new Button
            {
                Content = new FontIcon { Glyph = "\xE74D", FontSize = 10 },
                Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
                Padding = new Thickness(4),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTipService.SetToolTip(removeButton, new ToolTip
            {
                Content = removeTooltipText,
                Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
            });
            removeButton.Click += (s, e) =>
            {
                var updated = folders.Where(f => f != capturedFolder).ToList();
                WorkspaceFolders = updated.Count > 0 ? updated : null;
                WorkspaceFoldersChanged?.Invoke(this, updated);
            };
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);

            _folderList.Children.Add(row);
        }

        // Archive section — KB selector (always visible, hint when profile doesn't enable RAG)
        var archiveEnabled = IsKnowledgeArchiveEnabled;
        var selectedKbId = KnowledgeArchiveFolder; // stores KB ID

        if (_kbSelector is not null)
        {
            _kbSelector.Visibility = Visibility.Visible;

            // Populate KB list
            var kbItems = KbItemsProvider?.Invoke() ?? [];
            _kbSelector.ItemsSource = kbItems;

            // Restore selection
            if (!string.IsNullOrEmpty(selectedKbId))
            {
                var match = kbItems.FirstOrDefault(k => k.Id == selectedKbId);
                if (match is not null)
                    _kbSelector.SelectedItem = match;
            }

            if (_archiveEmpty is not null)
                _archiveEmpty.Visibility = kbItems.Count == 0
                    ? Visibility.Visible : Visibility.Collapsed;
        }

        // Hint when profile doesn't have RAG enabled (but KB is selected)
        if (_archiveDisabledHint is not null)
            _archiveDisabledHint.Visibility = !archiveEnabled && !string.IsNullOrEmpty(selectedKbId)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Handles <see cref="ChatZoomFactor"/> changes — applies CSS zoom and adjusts max-width.
    /// </summary>
    private static void OnChatZoomFactorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._isInitialized)
            panel.ApplyChatZoom();
    }

    /// <summary>
    /// Applies the current <see cref="ChatZoomFactor"/> to the WebView2 via CSS zoom
    /// and compensates <c>#chat-container</c> max-width to keep visual width at 800px.
    /// </summary>
    private void ApplyChatZoom()
    {
        _ = _renderer.ApplyZoomAsync(ChatZoomFactor);
    }

    /// <summary>
    /// Updates the indexing progress UI elements in the flyout and title bar.
    /// Call this after changing <see cref="IsArchiveIndexing"/>, <see cref="ArchiveIndexingProgress"/>,
    /// or <see cref="ArchiveIndexingText"/>.
    /// </summary>
    public void UpdateArchiveProgressUI()
    {
        // Indexing progress is now managed by the KB Settings page.
        // This method is kept for API compatibility but is a no-op.
    }

    /// <summary>
    /// Applies localized strings to flyout elements that cannot use x:Uid in a ControlTemplate.
    /// </summary>
    private void LocalizeFlyoutText(FrameworkElement root)
    {
        SetText(root, "PART_FolderHeaderText", "Folder_Header", "Workspace Folders");
        SetText(root, "PART_FolderAddButtonText", "Folder_AddButton", "Add Folder");
        SetText(root, "PART_ArchiveHeaderText", "Folder_ArchiveHeader", "Knowledge Archive");

        // Elements inside Collapsed parents aren't in the visual tree yet,
        // so search within the referenced PART_ elements directly.
        if (_folderEmpty is not null)
            _folderEmpty.Text = Res.GetString("Folder_Empty") ?? "(empty)";
        if (_folderDisabledHint is not null)
            _folderDisabledHint.Text = Res.GetString("Folder_DisabledHint") ?? "Enable Workspace in your profile to use these folders.";
        if (_archiveDisabledHint is not null)
            _archiveDisabledHint.Text = Res.GetString("Folder_ArchiveDisabledHint") ?? "Current profile does not have Knowledge Archive enabled.";
        if (_archiveEmpty is not null)
            _archiveEmpty.Text = Res.GetString("Folder_ArchiveNoKbs") ?? "No knowledge bases. Create one in Settings.";

        static void SetText(FrameworkElement parent, string elementName, string resKey, string fallback)
        {
            var el = FindDescendantByName<TextBlock>(parent, elementName);
            if (el is not null)
                el.Text = Res.GetString(resKey) ?? fallback;
        }
    }

    /// <summary>
    /// Finds a descendant element by name using breadth-first traversal of the visual tree.
    /// </summary>
    private static T? FindDescendantByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed && typed.Name == name)
                return typed;

            var result = FindDescendantByName<T>(child, name);
            if (result is not null)
                return result;
        }
        return null;
    }

    /// <summary>
    /// Updates the title text, showing a greeting when no conversation is active
    /// or the actual title when a conversation has started.
    /// Also toggles edit/refresh button visibility accordingly.
    /// </summary>
    private void UpdateTitleDisplay()
    {
        if (_titleText is null) return;

        if (_isConversationActive)
        {
            _titleText.Text = Title ?? "";
            if (_titleEditButton is not null) _titleEditButton.Visibility = Visibility.Visible;
            if (_titleRefreshButton is not null) _titleRefreshButton.Visibility = Visibility.Visible;
        }
        else
        {
            _greetingText ??= LoadGreeting();
            _titleText.Text = _greetingText;
            if (_titleEditButton is not null) _titleEditButton.Visibility = Visibility.Collapsed;
            if (_titleRefreshButton is not null) _titleRefreshButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Loads the greeting text from resources.
    /// </summary>
    private static string LoadGreeting()
    {
        return Res.GetString("ChatPanel_Greeting") ?? "How can I help you today?";
    }

    /// <summary>
    /// Updates the folder button visual state to indicate whether any folders are configured.
    /// Uses VisualStateManager so {ThemeResource} handles theme changes automatically.
    /// </summary>
    private void UpdateFolderButtonBadge()
    {
        var hasFolders = (WorkspaceFolders?.Count ?? 0) > 0
            || !string.IsNullOrEmpty(KnowledgeArchiveFolder);

        VisualStateManager.GoToState(this, hasFolders ? "HasFolders" : "NoFolders", true);
    }

    /// <summary>
    /// Updates the folder button appearance based on current state.
    /// </summary>
    private void UpdateFolderButtonAppearance()
    {
        UpdateFolderButtonBadge();
    }

    /// <summary>
    /// Handles the DragOver event to accept file drop operations.
    /// </summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    /// <summary>
    /// Handles the Drop event to add dropped files as attachments.
    /// </summary>
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        var items = await e.DataView.GetStorageItemsAsync();
        if (_inputArea is not null)
            await _inputArea.AddFilesAsync(items);
    }

    #endregion

    #region Tree Methods

    /// <summary>
    /// Registers a message in the conversation tree.
    /// When <paramref name="updateActiveChild"/> is <c>true</c>, the parent message's
    /// <see cref="ChatMessage.ActiveChildId"/> is set to this message so that
    /// save/restore preserves the user's branch selection.
    /// </summary>
    private void RegisterInTree(ChatMessage msg, bool updateActiveChild = true)
    {
        var key = msg.ParentId ?? TreeRootKey;
        if (!_childrenMap.TryGetValue(key, out var siblings))
        {
            siblings = [];
            _childrenMap[key] = siblings;
        }
        if (!siblings.Any(s => s.Id == msg.Id))
        {
            msg.SiblingIndex = siblings.Count;
            siblings.Add(msg);
        }

        // Tool-internal messages (assistant tool-call requests and tool results) are
        // not real branch points — they are part of the same response chain.
        // Exclude them from the visible sibling count so the UI does not show
        // spurious branch-navigation arrows when a tool chain lives alongside the
        // next user message under the same parent.
        static bool IsToolInternal(ChatMessage m) =>
            m.Role == ChatRole.Tool ||
            (m.Role == ChatRole.Assistant && m.ToolCalls is { Count: > 0 });

        var visibleCount = siblings.Count(s => !IsToolInternal(s));
        foreach (var s in siblings)
            s.SiblingCount = IsToolInternal(s) ? 1 : Math.Max(visibleCount, 1);

        // Update the parent's active child pointer when on the active path
        if (updateActiveChild && msg.ParentId is not null)
        {
            var parent = _messages.FirstOrDefault(m => m.Id == msg.ParentId);
            if (parent is not null) parent.ActiveChildId = msg.Id;
        }
    }

    /// <summary>
    /// Builds a path from the given message to its deepest leaf,
    /// always following the last child (most recent branch) at each level.
    /// </summary>
    private List<ChatMessage> BuildPathToLeaf(ChatMessage start)
    {
        var path = new List<ChatMessage> { start };
        var current = start;
        while (_childrenMap.TryGetValue(current.Id, out var children) && children.Count > 0)
        {
            current = children[^1]; // follow last (most recent) child
            path.Add(current);
        }
        return path;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Returns the currently active tools, filtered by the input area's enabled tool selection.
    /// Tool policy (search_tools substitution, etc.) is applied at the App layer (ResolveTools).
    /// </summary>
    private IReadOnlyList<IAssistTool> GetActiveTools()
    {
        var enabledNames = _inputArea?.EnabledToolNames;
        return enabledNames is null
            ? RegisteredTools
            : [.. RegisteredTools.Where(t => enabledNames.Contains(t.Name))];
    }

    private record StreamResult(TokenUsage? Usage, bool IsTruncated, IReadOnlyList<ToolCall>? ToolCalls = null)
    {
        public bool HasToolCalls => ToolCalls is { Count: > 0 };
    }

    /// <summary>
    /// Batched rendering instruction sent from the background producer to the UI-thread consumer.
    /// </summary>
    private abstract record RenderCommand
    {
        /// <summary>Flush accumulated text tokens to the assistant message.</summary>
        public sealed record FlushText(string Text) : RenderCommand;

        /// <summary>Begin a collapsible thinking block (must precede FlushThinking).</summary>
        public sealed record BeginThinking : RenderCommand;

        /// <summary>Flush accumulated thinking tokens.</summary>
        public sealed record FlushThinking(string Text) : RenderCommand;
    }

    /// <summary>
    /// Inspects a tool result and returns a size-guarded version to prevent context overflow.
    /// Binary/base64 content is replaced with metadata; oversized text is truncated.
    /// Error results (short JSON with "error" key) pass through unchanged.
    /// </summary>
    /// <summary>
    /// Logs structured summary for RAG tool results (search_documents, get_document_chunk).
    /// </summary>
    private static void LogStructuredToolResult(string toolName, string resultJson)
    {
        try
        {
            if (toolName == "search_documents")
            {
                using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
                var root = doc.RootElement;
                var mode = root.TryGetProperty("search_mode", out var m) ? m.GetString() : "?";
                var total = root.TryGetProperty("total_chunks_searched", out var t) ? t.GetInt32() : 0;
                var results = root.TryGetProperty("results", out var r) ? r : default;
                var count = results.ValueKind == System.Text.Json.JsonValueKind.Array ? results.GetArrayLength() : 0;

                DiagnosticLogger.LogInfo(
                    $"[Tool] Result: search_documents → mode={mode}, {count} results, {total} chunks");

                if (results.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var i = 1;
                    foreach (var item in results.EnumerateArray())
                    {
                        var src = item.TryGetProperty("source_path", out var s)
                            ? System.IO.Path.GetFileName(s.GetString() ?? "") : "?";
                        var ci = item.TryGetProperty("chunk_index", out var c) ? c.GetInt32() : -1;
                        var score = item.TryGetProperty("score", out var sc) ? sc.GetDouble() : 0;
                        DiagnosticLogger.LogInfo($"  #{i} {src} [chunk {ci}] score={score:F3}");
                        i++;
                    }
                }
            }
            else if (toolName == "get_document_chunk")
            {
                using var doc = System.Text.Json.JsonDocument.Parse(resultJson);
                var root = doc.RootElement;
                var chunkId = root.TryGetProperty("chunk_id", out var id) ? id.ToString() : "?";
                var src = root.TryGetProperty("source_path", out var s)
                    ? System.IO.Path.GetFileName(s.GetString() ?? "") : "?";
                var ci = root.TryGetProperty("chunk_index", out var c) ? c.GetInt32() : -1;
                DiagnosticLogger.LogInfo(
                    $"[Tool] Result: get_document_chunk → id={chunkId}, {src} [chunk {ci}]");
            }
        }
        catch
        {
            // JSON parsing failure — the existing length log is sufficient
        }
    }

    /// <summary>
    /// Formats a rich label for fetch_url tool blocks: "fetch_url("url") — N chars".
    /// Falls back to plain "fetch_url" on parse failure.
    /// </summary>
    private static string FormatFetchUrlLabel(string argumentsJson, int resultLength)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("url", out var urlProp))
            {
                var url = urlProp.GetString() ?? "?";
                return $"fetch_url(\u201C{url}\u201D) \u2014 {resultLength:N0} chars";
            }
        }
        catch { /* fall through */ }
        return "fetch_url";
    }

    /// <summary>
    /// Checks if a delegate_task call targets a registered specialist.
    /// Only returns true if the specialist name is validated by
    /// <see cref="IsRegisteredSpecialist"/>, preventing AI from bypassing
    /// approval by injecting arbitrary specialist names.
    /// </summary>
    private bool IsRegisteredSpecialistCall(string argumentsJson)
    {
        var name = GetSpecialistName(argumentsJson);
        return name is not null && IsRegisteredSpecialist?.Invoke(name) == true;
    }

    /// <summary>
    /// Checks if a delegate_task call has a specialist field (for UI labeling).
    /// </summary>
    private static string? GetSpecialistName(string argumentsJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("specialist", out var sp))
                return sp.GetString();
        }
        catch { /* fall through */ }
        return null;
    }

    /// <summary>
    /// Gets the display name for a specialist, or null if not found.
    /// </summary>
    private string? GetSpecialistDisplayName(string specialistName)
        => SpecialistDisplayNameResolver?.Invoke(specialistName);

    private string FormatSpecialistLabel(string argumentsJson, string resultJson)
    {
        var specialistName = GetSpecialistName(argumentsJson);
        var displayName = specialistName is not null
            ? GetSpecialistDisplayName(specialistName) ?? "Specialist"
            : "Specialist";

        var promptSnippet = displayName;
        var statusIcon = "\u2713"; // ✓ default

        try
        {
            using var argsDoc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (argsDoc.RootElement.TryGetProperty("prompt", out var promptProp))
            {
                var prompt = promptProp.GetString() ?? "";
                promptSnippet = prompt.Length > 40
                    ? $"\uD83D\uDD0D {displayName}: {prompt[..40]}\u2026"
                    : $"\uD83D\uDD0D {displayName}: {prompt}";
            }
        }
        catch { /* fall through */ }

        try
        {
            using var resultDoc = System.Text.Json.JsonDocument.Parse(resultJson);
            if (resultDoc.RootElement.TryGetProperty("status", out var statusProp))
            {
                statusIcon = statusProp.GetString() switch
                {
                    "completed" => "\u2713",           // ✓
                    "failed" => "\u2717",               // ✗
                    "timed_out" => "\u23F1",            // ⏱
                    "max_rounds_reached" => "\u26A0",   // ⚠
                    _ => "\u2713",
                };
            }
        }
        catch { /* fall through */ }

        return $"{promptSnippet} {statusIcon}";
    }

    private static string FormatSubAgentLabel(string argumentsJson, string resultJson)
    {
        // Extract prompt (truncated) and status from sub-agent call
        var promptSnippet = "Sub-Agent";
        var statusIcon = "\u2713"; // ✓ default

        try
        {
            using var argsDoc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            if (argsDoc.RootElement.TryGetProperty("prompt", out var promptProp))
            {
                var prompt = promptProp.GetString() ?? "";
                promptSnippet = prompt.Length > 40
                    ? $"Sub-Agent: {prompt[..40]}\u2026"
                    : $"Sub-Agent: {prompt}";
            }
        }
        catch { /* fall through */ }

        try
        {
            using var resultDoc = System.Text.Json.JsonDocument.Parse(resultJson);
            if (resultDoc.RootElement.TryGetProperty("status", out var statusProp))
            {
                statusIcon = statusProp.GetString() switch
                {
                    "completed" => "\u2713",           // ✓
                    "failed" => "\u2717",               // ✗
                    "timed_out" => "\u23F1",            // ⏱
                    "max_rounds_reached" => "\u26A0",   // ⚠
                    _ => "\u2713",
                };
            }
        }
        catch { /* fall through */ }

        return $"{promptSnippet} {statusIcon}";
    }

    private static string GuardToolResultSize(string toolResult, string toolName)
    {
        // Short results and error results pass through unchanged
        if (toolResult.Length <= MaxToolResultChars)
            return toolResult;

        if (toolResult.Length < 500 && toolResult.Contains("\"error\"", StringComparison.Ordinal))
            return toolResult;

        // Detect binary/base64 content — replace entirely with metadata
        if (IsBinaryContent(toolResult))
        {
            DiagnosticLogger.LogWarning(
                $"[Tool] Binary content detected: {toolName}, {toolResult.Length:N0} chars — replaced with metadata");
            return $"""
                [Binary/encoded content detected]
                Tool: {toolName}
                Original size: {toolResult.Length:N0} chars

                This result contains binary or base64-encoded content that cannot be used inline.
                Use DocumentParsers-integrated tools to extract text content, or read a specific section.
                """;
        }

        // Text content exceeding threshold — truncate with guidance
        DiagnosticLogger.LogWarning(
            $"[Tool] Result truncated: {toolName}, {toolResult.Length:N0} -> {MaxToolResultChars:N0} chars");
        return string.Concat(
            toolResult.AsSpan(0, MaxToolResultChars),
            $"\n\n--- truncated ---\nOriginal size: {toolResult.Length:N0} chars. Showing first {MaxToolResultChars:N0} chars.\nUse a more specific query or read a specific section.");
    }

    /// <summary>
    /// Detects whether the content is likely binary or base64-encoded.
    /// </summary>
    private static bool IsBinaryContent(ReadOnlySpan<char> content)
    {
        // Check for null characters (strong binary indicator)
        if (content.Contains('\0'))
            return true;

        // Check for base64 pattern: long string of [A-Za-z0-9+/=] with no newlines
        if (content.Length >= Base64DetectionThreshold)
        {
            // Sample the first portion for base64 characteristics
            var sample = content[..Math.Min(1000, content.Length)];
            var base64Chars = 0;
            foreach (var c in sample)
            {
                if (char.IsLetterOrDigit(c) || c is '+' or '/' or '=')
                    base64Chars++;
            }

            // If >90% of sampled chars are base64-alphabet, likely encoded
            if (base64Chars > sample.Length * 0.9)
                return true;
        }

        // Check control character ratio in a sample
        var ctrlSample = content[..Math.Min(2000, content.Length)];
        var controlChars = 0;
        foreach (var c in ctrlSample)
        {
            if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
                controlChars++;
        }

        return controlChars > ctrlSample.Length * 0.05; // >5% control chars
    }

    /// <summary>
    /// Consumes a stream of <see cref="StreamEvent"/> instances on a background thread,
    /// forwarding batched rendering commands to the UI thread via a <see cref="Channel{T}"/>.
    /// Returns aggregated usage, truncation info, and any tool calls.
    /// </summary>
    private async Task<StreamResult> ConsumeStreamAsync(
        IAsyncEnumerable<StreamEvent> events, ChatMessage message, CancellationToken ct)
    {
        TokenUsage? usage = null;
        var isTruncated = false;
        var toolAccumulator = new StreamToolCallAccumulator();
        var thinkingBlockStarted = false;

        var channel = Channel.CreateUnbounded<RenderCommand>(
            new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

        // ── Producer (background thread) ──────────────────────────────
        var producer = Task.Run(async () =>
        {
            try
            {
                var textBatch = new System.Text.StringBuilder();
                var thinkingBatch = new System.Text.StringBuilder();
                var lastFlush = Environment.TickCount64;

                await foreach (var evt in events.WithCancellation(ct))
                {
                    switch (evt)
                    {
                        case StreamEvent.ThinkingDelta thinking:
                            if (!thinkingBlockStarted)
                            {
                                if (textBatch.Length > 0)
                                {
                                    channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));
                                    textBatch.Clear();
                                }
                                channel.Writer.TryWrite(new RenderCommand.BeginThinking());
                                thinkingBlockStarted = true;
                            }
                            thinkingBatch.Append(thinking.Text);
                            break;

                        case StreamEvent.TextDelta delta:
                            textBatch.Append(delta.Text);
                            break;

                        case StreamEvent.ToolCallStart start:
                            toolAccumulator.HandleStart(start);
                            break;

                        case StreamEvent.ToolCallDelta delta:
                            toolAccumulator.HandleDelta(delta);
                            break;

                        case StreamEvent.Usage u:
                            usage = u.TokenUsage;
                            break;

                        case StreamEvent.StreamCompleted completed:
                            isTruncated = completed.IsTruncated;
                            break;
                    }

                    var now = Environment.TickCount64;
                    if (now - lastFlush >= 50)
                    {
                        if (thinkingBatch.Length > 0)
                        {
                            channel.Writer.TryWrite(new RenderCommand.FlushThinking(thinkingBatch.ToString()));
                            thinkingBatch.Clear();
                        }
                        if (textBatch.Length > 0)
                        {
                            channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));
                            textBatch.Clear();
                        }
                        lastFlush = now;
                    }
                }

                // Final flush
                if (thinkingBatch.Length > 0)
                    channel.Writer.TryWrite(new RenderCommand.FlushThinking(thinkingBatch.ToString()));
                if (textBatch.Length > 0)
                    channel.Writer.TryWrite(new RenderCommand.FlushText(textBatch.ToString()));

                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
                throw;
            }
        }, ct);

        // ── Consumer (UI thread) ──────────────────────────────────────
        try
        {
            while (await channel.Reader.WaitToReadAsync(ct))
            {
                while (channel.Reader.TryRead(out var cmd))
                {
                    switch (cmd)
                    {
                        case RenderCommand.FlushText ft:
                            message.Content += ft.Text;
                            await _renderer.AppendTokenAsync(message.Id, ft.Text);
                            break;

                        case RenderCommand.BeginThinking:
                            await _renderer.BeginThinkingBlockAsync(message.Id);
                            break;

                        case RenderCommand.FlushThinking ft:
                            message.ThinkingContent = (message.ThinkingContent ?? "") + ft.Text;
                            await _renderer.AppendThinkingTokenAsync(message.Id, ft.Text);
                            break;
                    }
                }

                await Task.Delay(50, ct);
            }
        }
        catch (ChannelClosedException)
        {
            // Producer exception — will be surfaced by await producer below
        }

        // Propagate producer exceptions (HTTP errors, parsing failures, etc.)
        await producer;

        if (thinkingBlockStarted)
            await _renderer.EndThinkingBlockAsync(message.Id);

        var toolCalls = toolAccumulator.HasToolCalls ? toolAccumulator.Drain() : null;
        return new StreamResult(usage, isTruncated, toolCalls);
    }

    /// <summary>
    /// Streams an AI response and executes any tool calls, looping until the AI
    /// produces a text-only response or max rounds are reached. Unifies the previously
    /// separate streaming-only and tool-calling code paths.
    /// </summary>
    private async Task<StreamResult> StreamAndExecuteAsync(
        ChatMessage assistantMessage, CancellationToken ct)
    {
        ToolCallExecutor? executor = null;
        var activeTools = GetActiveTools();

        // Auto-connect servers and filter to connected tools before sending
        if (PrepareToolsForSendAsync is not null)
            activeTools = await PrepareToolsForSendAsync(activeTools);

        if (activeTools.Count > 0)
        {
            // Read McpTools after delegate (it may have updated connection-filtered tools)
            var executableTools = McpTools is { Count: > 0 } mcpTools
                ? [.. activeTools, .. mcpTools]
                : activeTools;
            executor = new ToolCallExecutor(executableTools);
            if (_approvalPanel is not null && _inputArea is not null)
            {
                executor.ConfirmationHandler = async (toolName, arguments) =>
                {
                    // Auto-approve specialist calls — validated against SpecialistRegistry
                    if (toolName == "delegate_task" && IsRegisteredSpecialistCall(arguments))
                    {
                        DiagnosticLogger.LogInfo($"[Tool] Specialist auto-approved: {toolName}");
                        return (true, null);
                    }

                    DiagnosticLogger.LogInfo($"[Tool] Approval requested: {toolName}");
                    _approvalPanel.ToolName = toolName;
                    _approvalPanel.ToolDisplayName = GetLocalizedToolName(toolName);
                    _approvalPanel.Arguments = arguments;
                    _approvalPanel.IsExpanded = false;
                    _inputArea.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _approvalPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;
                    _approvalPanel.FocusUserNote();

                    _approvalTcs = new TaskCompletionSource<(bool, string?)>();
                    var (approved, userNote) = await _approvalTcs.Task;
                    DiagnosticLogger.LogInfo($"[Tool] Approval result: {toolName} → {(approved ? "approved" : "rejected")}{(userNote is not null ? $" (note: {userNote})" : "")}");

                    _approvalPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _inputArea.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                    return (approved, userNote);
                };
            }
        }

        StreamResult result;
        var round = 0;
        string? pendingUserNote = null;

        do
        {
            // Inject user note from previous tool approval as a transient user message
            // (not persisted in _messages — only visible to the LLM in this API call)
            IReadOnlyList<ChatMessage> messages = pendingUserNote is not null
                ? [.. _messages, new ChatMessage(ChatRole.User, pendingUserNote)]
                : [.. _messages];
            pendingUserNote = null;
            var request = await CreateRequestAsync(messages, activeTools);
            if (round == 0)
                DiagnosticLogger.LogInfo($"[Chat] Request start — provider={Provider!.ProviderName}, model={Provider.ModelId}, tools={activeTools.Count}, thinking={request.ThinkingEnabled}");
            result = await ConsumeStreamAsync(Provider!.StreamAsync(request, ct), assistantMessage, ct);
            DiagnosticLogger.LogInfo($"[Chat] Stream completed — tokens={result.Usage?.TotalTokens ?? 0}, truncated={result.IsTruncated}, hasToolCalls={result.HasToolCalls}");

            if (!result.HasToolCalls || executor is null)
                break;

            round++;
            var delegateCount = result.ToolCalls!.Count(tc => tc.FunctionName == "delegate_task");
            DiagnosticLogger.LogInfo($"[Chat] Tool round {round}/{MaxToolCallRounds}, toolCalls={result.ToolCalls!.Count}"
                + (delegateCount > 1 ? $", delegate_task×{delegateCount} (parallel candidate)" : ""));
            if (round > MaxToolCallRounds)
            {
                DiagnosticLogger.LogWarning($"[Tool] Max tool call rounds ({MaxToolCallRounds}) exceeded, stopping");
                break;
            }

            // Add the assistant's tool call message to history
            var toolCallParentId = _messages.Count > 0 ? _messages[^1].Id : null;
            var toolCallMsg = new ChatMessage(ChatRole.Assistant)
            {
                ToolCalls = result.ToolCalls,
                Content = assistantMessage.Content,
                ProviderName = Provider.ProviderName,
                ProviderModelId = Provider.ModelId,
                ParentId = toolCallParentId
            };
            RegisterInTree(toolCallMsg);
            _messages.Add(toolCallMsg);

            // Split tool calls into regular and sub-agent groups
            var subAgentCalls = result.ToolCalls!.Where(tc => tc.FunctionName == "delegate_task").ToList();
            var otherCalls = result.ToolCalls!.Where(tc => tc.FunctionName != "delegate_task").ToList();

            // Phase 1: Regular tools — sequential execution (existing behavior)
            foreach (var call in otherCalls)
            {
                DiagnosticLogger.LogInfo($"[Tool] Executing: {call.FunctionName} (id={call.Id})");
                ToolExecutionResult execResult;
                try
                {
                    execResult = await executor.ExecuteAsync(call, ct);
                    DiagnosticLogger.LogInfo($"[Tool] Result: {call.FunctionName}, length={execResult.Text.Length}");
                    LogStructuredToolResult(call.FunctionName, execResult.Text);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogWarning($"[Tool] Execution error: {call.FunctionName} — {ex.Message}");
                    DiagnosticLogger.LogException(ex);
                    execResult = new ToolExecutionResult($"{{\"error\":\"{ex.Message}\"}}");
                }

                if (executor.LastUserNote is not null)
                    pendingUserNote = executor.LastUserNote;

                var toolResult = GuardToolResultSize(execResult.Text, call.FunctionName);

                var toolResultMsg = new ChatMessage(ChatRole.Tool, toolResult)
                {
                    ToolCallId = call.Id,
                    ParentId = toolCallMsg.Id
                };
                RegisterInTree(toolResultMsg);
                _messages.Add(toolResultMsg);

                assistantMessage.Content += $"[Tool: {call.FunctionName}]\n";

                if (call.FunctionName == "search_documents")
                    await _renderer.AppendSearchResultBlockAsync(assistantMessage.Id, toolResult, call.FunctionName);
                else if (call.FunctionName == "fetch_url")
                    await _renderer.AppendToolBlockAsync(assistantMessage.Id, FormatFetchUrlLabel(call.Arguments, toolResult.Length));
                else
                    await _renderer.AppendToolBlockAsync(assistantMessage.Id, call.FunctionName);

                if (execResult.ImageDataUris is { Count: > 0 } imageUris)
                {
                    foreach (var dataUri in imageUris)
                        await _renderer.AppendToolImageAsync(assistantMessage.Id, dataUri);
                }
            }

            // Phase 2: Sub-Agent calls — approve sequentially, execute in parallel
            if (subAgentCalls.Count > 0)
            {
                var approved = new List<(ToolCall Call, string? UserNote, Task<ToolExecutionResult> Task)>();
                var rejected = new List<(ToolCall Call, string RejectionText)>();

                // 2a: Sequential approval, immediate execution start
                foreach (var call in subAgentCalls)
                {
                    DiagnosticLogger.LogInfo($"[Tool] Sub-Agent approval: {call.FunctionName} (id={call.Id})");

                    if (executor.ConfirmationHandler is not null)
                    {
                        var (isApproved, userNote) = await executor.ConfirmationHandler(call.FunctionName, call.Arguments);
                        if (isApproved)
                        {
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent approved, starting parallel: {call.Id}");
                            var task = executor.ExecuteWithoutConfirmationAsync(call, userNote, ct);
                            approved.Add((call, userNote, task));
                        }
                        else
                        {
                            var reason = string.IsNullOrWhiteSpace(userNote)
                                ? "Tool call rejected by user."
                                : $"Tool call rejected by user. Reason: {userNote}";
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent rejected: {call.Id} — {reason}");
                            rejected.Add((call, reason));
                        }
                    }
                    else
                    {
                        var task = executor.ExecuteWithoutConfirmationAsync(call, null, ct);
                        approved.Add((call, null, task));
                    }
                }

                // 2b: Wait for all parallel executions to complete
                if (approved.Count > 0)
                {
                    DiagnosticLogger.LogInfo($"[Tool] Awaiting {approved.Count} parallel sub-agent tasks");
                    try { await Task.WhenAll(approved.Select(a => a.Task)); }
                    catch { /* individual errors handled in 2c */ }
                }

                // 2c: Collect results in original call order
                foreach (var call in subAgentCalls)
                {
                    ToolExecutionResult execResult;
                    string? noteForPending = null;

                    var approvedEntry = approved.FirstOrDefault(a => a.Call.Id == call.Id);
                    if (approvedEntry.Task is not null)
                    {
                        try
                        {
                            execResult = await approvedEntry.Task;
                            DiagnosticLogger.LogInfo($"[Tool] Sub-Agent result: {call.Id}, length={execResult.Text.Length}");
                            LogStructuredToolResult(call.FunctionName, execResult.Text);
                            noteForPending = approvedEntry.UserNote;
                        }
                        catch (Exception ex)
                        {
                            DiagnosticLogger.LogWarning($"[Tool] Sub-Agent error: {call.Id} — {ex.Message}");
                            DiagnosticLogger.LogException(ex);
                            execResult = new ToolExecutionResult($"{{\"error\":\"{ex.Message}\"}}");
                        }
                    }
                    else
                    {
                        var rejectedEntry = rejected.First(r => r.Call.Id == call.Id);
                        execResult = new ToolExecutionResult(rejectedEntry.RejectionText);
                    }

                    if (noteForPending is not null)
                        pendingUserNote = noteForPending;

                    var toolResult = GuardToolResultSize(execResult.Text, call.FunctionName);
                    var toolResultMsg = new ChatMessage(ChatRole.Tool, toolResult)
                    {
                        ToolCallId = call.Id,
                        ParentId = toolCallMsg.Id
                    };
                    RegisterInTree(toolResultMsg);
                    _messages.Add(toolResultMsg);

                    assistantMessage.Content += $"[Tool: {call.FunctionName}]\n";
                    var subAgentLabel = GetSpecialistName(call.Arguments) is not null
                        ? FormatSpecialistLabel(call.Arguments, toolResult)
                        : FormatSubAgentLabel(call.Arguments, toolResult);
                    await _renderer.AppendToolBlockAsync(assistantMessage.Id, subAgentLabel);

                    if (execResult.ImageDataUris is { Count: > 0 } imageUris)
                    {
                        foreach (var dataUri in imageUris)
                            await _renderer.AppendToolImageAsync(assistantMessage.Id, dataUri);
                    }
                }
            }
        } while (true);

        return result;
    }

    /// <summary>
    /// Sets a tooltip with <see cref="Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse"/> placement on the specified element.
    /// </summary>
    private static void SetBottomRightToolTip(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, new ToolTip
        {
            Content = text,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
        });
    }

    /// <summary>
    /// Updates the summarize button enabled state based on the current message count.
    /// </summary>
    private void UpdateSummarizeButtonState()
    {
        if (_inputArea is not null)
            _inputArea.IsSummarizeEnabled = _messages.Count > RecentTurnsToKeep;
    }

    /// <summary>
    /// Renders all pre-existing messages in <see cref="_messages"/> to the WebView2.
    /// Called from <see cref="OnLoaded"/> and from the App layer after pending messages are flushed
    /// when messages arrive after initialization.
    /// </summary>
    public async Task RenderRestoredMessagesAsync()
    {
        if (!_isInitialized || _messages.Count == 0 || _hasRenderedRestored) return;
        _hasRenderedRestored = true;

        DiagnosticLogger.LogInfo($"[Chat] RenderRestoredMessages: {_messages.Count} messages");
        SwitchToChatLayout();

        // Pre-index tool results for rich UI restoration.
        // Uses linear _messages scan + explicit ToolCallId matching — no tree traversal.
        var searchResultQueue = new Queue<string>();
        var fetchUrlLabelQueue = new Queue<string>();
        var subAgentLabelQueue = new Queue<string>();
        foreach (var m in _messages)
        {
            if (m.Role != ChatRole.Assistant || m.ToolCalls is not { Count: > 0 })
                continue;
            foreach (var tc in m.ToolCalls)
            {
                var resultMsg = _messages.FirstOrDefault(r =>
                    r.Role == ChatRole.Tool && r.ToolCallId == tc.Id);

                if (tc.FunctionName == "search_documents" && resultMsg?.Content is not null)
                    searchResultQueue.Enqueue(resultMsg.Content);
                else if (tc.FunctionName == "fetch_url")
                    fetchUrlLabelQueue.Enqueue(FormatFetchUrlLabel(tc.Arguments, resultMsg?.Content?.Length ?? 0));
                else if (tc.FunctionName == "delegate_task")
                    subAgentLabelQueue.Enqueue(FormatSubAgentLabel(tc.Arguments, resultMsg?.Content ?? ""));
            }
        }

        var renderedCount = 0;
        foreach (var msg in _messages)
        {
            if (msg.Role == ChatRole.User)
            {
                await _renderer.AppendUserMessageAsync(
                    msg.Id, msg.Content ?? "", msg.Timestamp.ToString("O"), msg.Attachments,
                    msg.SiblingIndex, msg.SiblingCount);
                renderedCount++;
            }
            else if (msg.Role == ChatRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                // Intermediate tool-call message — exists for API re-submission only, skip rendering.
            }
            else if (msg.Role == ChatRole.Assistant)
            {
                await _renderer.BeginAssistantMessageAsync(
                    msg.Id, msg.ProviderName, msg.ProviderModelId);

                var toolMarkers = ToolMarkerRegex().Matches(msg.Content ?? "");
                foreach (Match m in toolMarkers)
                {
                    var toolName = m.Groups[1].Value.Trim();

                    if (toolName == "search_documents" && searchResultQueue.Count > 0)
                    {
                        await _renderer.AppendSearchResultBlockAsync(msg.Id, searchResultQueue.Dequeue(), toolName);
                        continue;
                    }

                    if (toolName == "fetch_url" && fetchUrlLabelQueue.Count > 0)
                    {
                        await _renderer.AppendToolBlockAsync(msg.Id, fetchUrlLabelQueue.Dequeue());
                        continue;
                    }

                    if (toolName == "delegate_task")
                    {
                        var label = subAgentLabelQueue.Count > 0
                            ? subAgentLabelQueue.Dequeue()
                            : "Sub-Agent";
                        await _renderer.AppendToolBlockAsync(msg.Id, label);
                        continue;
                    }

                    await _renderer.AppendToolBlockAsync(msg.Id, toolName);
                }

                await _renderer.FinalizeMessageAsync(msg.Id, msg.Content ?? "");
                renderedCount++;
            }
            // Tool role — exists for API re-submission only, skip rendering.
        }
        DiagnosticLogger.LogInfo($"[Chat] RenderRestoredMessages: rendered {renderedCount}/{_messages.Count}");
    }

    /// <summary>
    /// Transitions the layout from the empty state panel to the active chat layout.
    /// </summary>
    private void SwitchToChatLayout()
    {
        if (_chatLayout is null || _emptyStatePanel is null ||
            _emptyStateContent is null || _inputArea is null) return;
        if (_chatLayout.Visibility == Microsoft.UI.Xaml.Visibility.Visible) return;

        _emptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        _chatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

        _isConversationActive = true;
        UpdateTitleDisplay();

        // Move InputArea from EmptyStatePanel into ChatLayout as Row 2
        _emptyStateContent.Children.Remove(_inputArea);
        _inputArea.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        Grid.SetRow(_inputArea, 2);
        _chatLayout.Children.Add(_inputArea);
    }

    /// <summary>
    /// Summarizes older conversation messages into a single system message to reduce token usage.
    /// </summary>
    private async Task SummarizeHistoryAsync(CancellationToken ct)
    {
        if (Provider is null) return;

        // Keep the most recent turns
        var turnsToKeep = Math.Min(RecentTurnsToKeep, _messages.Count);
        var oldMessages = _messages.Take(_messages.Count - turnsToKeep).ToList();
        if (oldMessages.Count == 0) return;
        DiagnosticLogger.LogInfo($"[Chat] Summarizing history — {oldMessages.Count} old messages, keeping {turnsToKeep}");

        // Build summarization prompt
        var historyText = string.Join("\n",
            oldMessages.Select(m => $"{m.Role}: {m.Content}"));

        var summaryRequest = new AiRequest
        {
            Messages =
            [
                new ChatMessage(ChatRole.User,
                    "Summarize the following conversation concisely, preserving key context and decisions:\n\n" +
                    historyText)
            ],
            SystemPrompt = "You are a helpful assistant that creates concise conversation summaries."
        };

        try
        {
            var summary = (await Provider.CompleteAsync(summaryRequest, ct)).Content ?? "";
            DiagnosticLogger.LogInfo("[Chat] History summarized successfully");

            // Replace old messages with a summary system message
            for (var i = 0; i < oldMessages.Count; i++)
            {
                _messages.RemoveAt(0);
            }

            _messages.Insert(0, new ChatMessage(ChatRole.System,
                $"[Previous conversation summary]\n{summary}"));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
        }
    }

    /// <summary>
    /// Streams a new assistant response for the given user message using the configured provider.
    /// </summary>
    private async Task StreamAssistantResponseAsync(ChatMessage userMessage)
    {
        if (Provider is null) return;

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId,
            ParentId = userMessage.Id
        };
        RegisterInTree(assistantMessage);
        _messages.Add(assistantMessage);
        await _renderer.BeginAssistantMessageAsync(assistantMessage.Id, Provider.ProviderName, Provider.ModelId);
        MessageAdded?.Invoke(this, assistantMessage);

        if (_inputArea is not null)
            _inputArea.IsInputEnabled = false;
        _streamingCts?.Cancel();
        _streamingCts = new CancellationTokenSource();
        var ct = _streamingCts.Token;

        try
        {
            var result = await StreamAndExecuteAsync(assistantMessage, ct);

            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content, result.IsTruncated, result.Usage?.TotalTokens ?? 0);

            if (IsDebugMode)
                await _renderer.SetDebugDataAsync(userMessage.Id, Provider.LastRequestBody, assistantMessage.Id, Provider.LastRawResponse);

            if (AutoSummarize && MaxInputTokens > 0 && result.Usage is { } usage &&
                usage.InputTokens > MaxInputTokens)
            {
                await SummarizeHistoryAsync(ct);
            }
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogInfo("[Chat] Streaming cancelled by user");
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            assistantMessage.Content += $"\n\n[Error: {ex.Message}]";
            await _renderer.FinalizeMessageAsync(assistantMessage.Id, assistantMessage.Content);
        }
        finally
        {
            assistantMessage.IsStreaming = false;
            if (_inputArea is not null)
            {
                _inputArea.IsInputEnabled = true;
                _inputArea.FocusInput();
            }
            UpdateSummarizeButtonState();
        }
    }

    /// <summary>
    /// Updates the input area placeholder text to include the provider name.
    /// </summary>
    private void UpdatePlaceholderWithProvider(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName))
        {
            var fallback = Res.GetString("ComposeBar_Placeholder");
            Placeholder = !string.IsNullOrEmpty(fallback) ? fallback : "Type a message...";
            return;
        }

        var format = Res.GetString("ComposeBar_AskProvider");
        if (!string.IsNullOrEmpty(format))
        {
            Placeholder = string.Format(format, providerName);
            return;
        }

        Placeholder = $"Ask {providerName}...";
    }

    /// <summary>
    /// Updates the title refresh button tooltip to include the provider name.
    /// </summary>
    private void UpdateRefreshTooltip()
    {
        var provider = UtilityProvider ?? Provider;
        var providerName = provider?.ProviderName ?? "";

        var format2 = Res.GetString("Chat_RegenerateTitle");
        if (!string.IsNullOrEmpty(format2) && !string.IsNullOrEmpty(providerName) && _titleRefreshButton is not null)
        {
            SetBottomRightToolTip(_titleRefreshButton, string.Format(format2, providerName));
            return;
        }

        if (_titleRefreshButton is not null)
        {
            SetBottomRightToolTip(_titleRefreshButton,
                string.IsNullOrEmpty(providerName)
                    ? "Regenerate title"
                    : $"Regenerate title with {providerName}");
        }
    }

    /// <summary>
    /// Attempts to auto-generate a title for the conversation after the first assistant response.
    /// </summary>
    private async void TryGenerateTitleAsync()
    {
        if (_titleGenerated || !AutoTitle) return;
        _titleGenerated = true;
        await GenerateTitleCoreAsync();
    }

    /// <summary>
    /// Core logic for generating or regenerating the conversation title using the utility or main provider.
    /// </summary>
    private async Task GenerateTitleCoreAsync()
    {
        var provider = UtilityProvider ?? Provider;
        if (provider is null or MockProvider) return;

        // Build context from conversation history
        var userMsg = _messages.FirstOrDefault(m => m.Role == ChatRole.User);
        var assistantMsg = _messages.FirstOrDefault(m => m.Role == ChatRole.Assistant);
        if (userMsg is null || assistantMsg is null) return;

        // Use more context for regeneration -- include recent messages
        var contextParts = new List<string>();
        foreach (var msg in _messages.Take(6))
        {
            var role = msg.Role == ChatRole.User ? "User" : "Assistant";
            var content = msg.Content.Length > 150 ? msg.Content[..150] : msg.Content;
            contextParts.Add($"{role}: {content}");
        }
        var context = string.Join("\n", contextParts);

        try
        {
            var titleRequest = new AiRequest
            {
                Messages =
                [
                    new ChatMessage(ChatRole.User,
                        $"{context}\n\nGenerate a short title (max 6 words) for this conversation. Reply with ONLY the title, no quotes or punctuation.")
                ],
                SystemPrompt = "You generate concise conversation titles.",
                Temperature = 0.5,
                MaxTokens = 200
            };

            var titleResponse = await provider.CompleteAsync(titleRequest);
            var title = (titleResponse.Content ?? "").Trim().Trim('"', '\'', '.').Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = userMsg.Content.Length > 40
                    ? userMsg.Content[..40].TrimEnd() + "\u2026"
                    : userMsg.Content;
            }

            DiagnosticLogger.LogInfo($"[Chat] Title generated: {title}");
            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, title));
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning("[Chat] Title generation failed, using fallback");
            DiagnosticLogger.LogException(ex);
            var fallback = userMsg.Content.Length > 40
                ? userMsg.Content[..40].TrimEnd() + "\u2026"
                : userMsg.Content;
            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, fallback));
        }
    }

    /// <summary>
    /// Applies the current theme (light or dark) to the root grid background and WebView renderer.
    /// </summary>
    private async Task ApplyThemeAsync()
    {
        if (!_isInitialized) return;

        var isDark = IsDarkTheme();
        if (_rootGrid is not null)
            _rootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(isDark ? DarkBg : LightBg);
        await _renderer.SetThemeAsync(isDark);
    }

    /// <summary>
    /// Loads localized UI strings from resources and pushes them to the WebView renderer.
    /// </summary>
    private async Task ApplyLocaleStringsAsync()
    {
        if (!_isInitialized) return;

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader(
                "AssistStudio.Controls/Resources");

            var strings = new Dictionary<string, string>
            {
                ["copy"] = loader.GetString("Chat_Copy"),
                ["copied"] = loader.GetString("Chat_Copied"),
                ["continue_label"] = loader.GetString("Chat_Continue"),
                ["code"] = loader.GetString("Chat_Code"),
                ["copyPrompt"] = loader.GetString("Chat_CopyPrompt"),
                ["copyMessage"] = loader.GetString("Chat_CopyMessage"),
                ["edit"] = loader.GetString("Chat_Edit"),
                ["retry"] = loader.GetString("Chat_Retry"),
                ["summarize"] = loader.GetString("Chat_Summarize"),
                ["copyRequest"] = loader.GetString("Chat_CopyRequest"),
                ["copyResponse"] = loader.GetString("Chat_CopyResponse"),
                ["tokens"] = loader.GetString("Chat_Tokens"),
                ["editBranchHint"] = loader.GetString("Chat_EditBranchHint"),
                ["editCancel"] = loader.GetString("Chat_EditCancel"),
                ["editSave"] = loader.GetString("Chat_EditSave"),
                ["showMore"] = loader.GetString("Chat_ShowMore"),
                ["showLess"] = loader.GetString("Chat_ShowLess"),
                ["imageSave"] = loader.GetString("Chat_ImageSave"),
                ["imageCopy"] = loader.GetString("Chat_ImageCopy"),
                ["imageExpand"] = loader.GetString("Chat_ImageExpand"),
                ["imageClose"] = loader.GetString("Chat_ImageClose"),
                ["imageSaved"] = loader.GetString("Chat_ImageSaved"),
                ["imageCopied"] = loader.GetString("Chat_ImageCopied"),
                ["seconds"] = loader.GetString("Chat_Seconds"),
                ["minutes"] = loader.GetString("Chat_Minutes"),
                ["hours"] = loader.GetString("Chat_Hours")
            };

            // Filter out empty strings (key not found returns empty)
            var validStrings = strings
                .Where(kv => !string.IsNullOrEmpty(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            if (validStrings.Count > 0)
            {
                await _renderer.SetLocaleStringsAsync(validStrings);
            }
        }
        catch
        {
            // Resource loading may fail if no .resw files are available (consumer app).
            // Defaults in chat.html will be used.
        }
    }

    /// <summary>
    /// Handles the Approved event from the ToolApprovalPanel.
    /// </summary>
    private void OnToolApproved(object? sender, string? userNote) => _approvalTcs?.TrySetResult((true, userNote));

    /// <summary>
    /// Handles the Rejected event from the ToolApprovalPanel.
    /// </summary>
    private void OnToolRejected(object? sender, string? userNote) => _approvalTcs?.TrySetResult((false, userNote));

    /// <summary>
    /// Returns a localized display name for a tool, falling back to the tool name.
    /// </summary>
    private string GetLocalizedToolName(string toolName)
    {
        var tool = RegisteredTools.FirstOrDefault(t => t.Name == toolName)
            ?? McpTools.FirstOrDefault(t => t.Name == toolName);
        if (tool is null) return toolName;

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader();
            var localized = loader.GetString($"Tool_{toolName}");
            return string.IsNullOrEmpty(localized) ? tool.DisplayName : localized;
        }
        catch
        {
            return tool.DisplayName;
        }
    }

    /// <summary>
    /// Builds an <see cref="AiRequest"/> from the current messages, selected preset settings,
    /// and optional workspace/RAG context.
    /// </summary>
    private async Task<AiRequest> CreateRequestAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<IAssistTool>? activeTools = null,
        string? systemPrompt = null)
    {
        activeTools ??= GetActiveTools();

        var workspaceText = WorkspaceContext is not null
            ? await WorkspaceContext.GetContextAsync()
            : null;

        // Append workspace folder paths so the AI knows absolute paths
        var folders = WorkspaceFolders;
        if (folders is { Count: > 0 })
        {
            var folderSection = "\n\n## Workspace\nCurrent workspace directories:\n"
                + string.Join("\n", folders.Select(f => $"- {f}"))
                + "\n\nWhen using `run_command` without an explicit working_directory, default to the first workspace directory listed above.";
            workspaceText = (workspaceText ?? "") + folderSection;
        }

        // Knowledge Archive hint — if search_documents tool is available and a KB is selected
        if (activeTools.Any(t => t.Name == "search_documents") && !string.IsNullOrEmpty(KnowledgeArchiveFolder))
        {
            var kbId = KnowledgeArchiveFolder;
            workspaceText = (workspaceText ?? "")
                + $"\n\n## Knowledge Archive\nUse `search_documents` to find relevant information before answering."
                + $"\nAlways pass kb_id=\"{kbId}\" when calling search_documents or get_document_chunk."
                + "\nIf initial search returns no results, retry with a lower threshold (e.g., 0.1) or different query terms.";
        }

        // Sub-Agent hint — when delegate_task tool is available
        if (activeTools.Any(t => t.Name == "delegate_task"))
        {
            workspaceText = (workspaceText ?? "")
                + "\n\n## Sub-Agent\n\n"
                + "You have a delegate_task tool that runs tasks in a separate context.\n\n"
                + "**ONLY delegate when ALL of these are true:**\n"
                + "- The task requires 5+ tool calls with intermediate reasoning\n"
                + "- The task is independent and does NOT need user clarification\n"
                + "- Running it in the main conversation would consume excessive context\n\n"
                + "**Do NOT delegate:**\n"
                + "- Simple lookups or searches (just call the tools directly)\n"
                + "- Tasks with fewer than 5 tool calls\n"
                + "- When you can answer by combining 2-3 tool results yourself\n\n"
                + "When delegating, specify allowed_tools to limit the sub-agent's tools.\n"
                + "You do NOT need to specify mcp_servers \u2014 inherited by default.\n"
                + "Multiple delegate_task calls in one response run as independent sub-tasks.";
        }

        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content;
        var chunks = ContextProvider is not null && lastUserMsg is not null
            ? await ContextProvider.RetrieveAsync(lastUserMsg)
            : null;

        var preset = SelectedPreset;
        return new AiRequest
        {
            Messages = messages,
            SystemPrompt = systemPrompt ?? SystemPrompt,
            MemoryText = MemoryText,
            WorkspaceText = workspaceText,
            ContextChunks = chunks is { Count: > 0 } ? chunks : null,
            Temperature = preset?.Temperature ?? 0.7,
            MaxTokens = preset?.MaxTokens ?? 4096,
            Tools = activeTools is { Count: > 0 } ? activeTools : null,
            ThinkingEnabled = preset?.ThinkingEnabled ?? false,
            ThinkingBudget = preset?.ThinkingBudget
        };
    }

    /// <summary>
    /// Determines whether the current theme is dark based on the <see cref="Theme"/> property or system setting.
    /// </summary>
    private bool IsDarkTheme() => Theme switch
    {
        ChatTheme.Light => false,
        ChatTheme.Dark => true,
        _ => ActualTheme == ElementTheme.Dark
    };

    #endregion

    #region Search

    /// <summary>
    /// Toggles the in-conversation search bar (Ctrl+F).
    /// </summary>
    public void ToggleSearchBar()
    {
        if (_searchBar is null) return;

        if (_searchBar.Visibility == Visibility.Visible)
        {
            CloseSearchBar();
        }
        else
        {
            _searchBar.Visibility = Visibility.Visible;
            _searchTextBox?.Focus(FocusState.Programmatic);
        }
    }

    private async void CloseSearchBar()
    {
        if (_searchBar is not null) _searchBar.Visibility = Visibility.Collapsed;
        if (_searchTextBox is not null) _searchTextBox.Text = string.Empty;
        if (_searchCount is not null) _searchCount.Text = string.Empty;
        _searchDebounceTimer?.Stop();

        if (_chatWebView?.CoreWebView2 is not null)
            await _chatWebView.ExecuteScriptAsync("window.assistChat.searchClear()").AsTask();
    }

    private async Task ExecuteSearchAsync(string query)
    {
        if (_chatWebView?.CoreWebView2 is null) return;

        var escaped = System.Text.Json.JsonSerializer.Serialize(query);
        var result = await _chatWebView.ExecuteScriptAsync(
            $"window.assistChat.search({escaped})").AsTask();

        UpdateSearchCount(result);
    }

    private async Task NavigateSearchAsync(int direction)
    {
        if (_chatWebView?.CoreWebView2 is null) return;

        var result = await _chatWebView.ExecuteScriptAsync(
            $"window.assistChat.searchNavigate({direction})").AsTask();

        UpdateSearchCount(result);
    }

    private void UpdateSearchCount(string? jsonResult)
    {
        if (_searchCount is null || jsonResult is null) return;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(
                jsonResult.Trim('"').Replace("\\\"", "\"").Replace("\\\\", "\\"));
            var root = doc.RootElement;
            var total = root.GetProperty("total").GetInt32();
            var current = root.GetProperty("current").GetInt32();
            _searchCount.Text = total > 0 ? $"{current}/{total}" : string.Empty;
        }
        catch
        {
            _searchCount.Text = string.Empty;
        }
    }

    #endregion

    #region Generated Regex

    /// <summary>Matches [Tool: ...] markers in assistant message content.</summary>
    [GeneratedRegex(@"\[Tool:\s*([^\]]+)\]")]
    private static partial Regex ToolMarkerRegex();

    #endregion
}

/// <summary>
/// Lightweight item for the KB selector ComboBox in the folder flyout.
/// </summary>
public sealed class KbItem
{
    /// <summary>Knowledge base UUID.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <inheritdoc/>
    public override string ToString() => Name;
}
