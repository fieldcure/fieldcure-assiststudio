using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Rendering;
using FieldCure.AssistStudio.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;

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
public sealed partial class ChatPanel : Control, IDisposable
{
    private static readonly ResourceLoader Res =
        new(ResourceLoader.GetDefaultResourceFilePath(), "AssistStudio.Controls/Resources");

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
    /// Tracks the currently active external assistant turn handle, if any.
    /// </summary>
    private AssistantTurnHandle? _currentTurn;

    /// <summary>
    /// Gets whether the WebView2 renderer has been initialized.
    /// Returns <c>false</c> after <see cref="Dispose"/> so callers can detect a recycled container.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    private bool _isInitialized;

    /// <summary>
    /// Guards against concurrent initialization from <see cref="OnLoaded"/> and
    /// <see cref="ReinitializeWebViewAsync"/>. Set before the first await in each path,
    /// so a second entry on the UI thread (after the await yields) sees the flag and returns.
    /// </summary>
    private bool _initializing;

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
    private TextBlock? _kbDisabledHint;
    private ComboBox? _kbSelector;
    private TextBlock? _kbEmpty;

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

    /// <summary>
    /// The panel shown in place of ComposeBar when an MCP server requests user input.
    /// </summary>
    private ToolElicitationPanel? _elicitationPanel;

    /// <summary>
    /// Completion source for awaiting user elicitation response.
    /// Action: "accept", "decline", or "cancel".
    /// </summary>
    private TaskCompletionSource<(string Action, IDictionary<string, object?>? Content)>? _elicitationTcs;

    private FrameworkElement? _searchBar;
    private TextBox? _searchTextBox;
    private TextBlock? _searchCount;
    private Button? _searchPrevButton;
    private Button? _searchNextButton;
    private Button? _searchCloseButton;
    private DispatcherTimer? _searchDebounceTimer;

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

    #region Dispose

    /// <summary>
    /// Releases WebView2 resources and removes the control from the visual tree.
    /// When TabView recycles this container for a new tab, <see cref="ReinitializeWebViewAsync"/>
    /// creates a fresh WebView2 instance — we never reuse a closed one.
    /// </summary>
    public void Dispose()
    {
        _streamingCts?.Cancel();
        _messages.Clear();
        _childrenMap.Clear();

        if (_chatWebView is not null)
        {
            try
            {
                // Remove from visual tree so the dead control doesn't occupy layout space
                if (_chatLayout is not null && _chatLayout.Children.Contains(_chatWebView))
                    _chatLayout.Children.Remove(_chatWebView);

                _chatWebView.CoreWebView2?.Navigate("about:blank");
                _chatWebView.Close();
            }
            catch (ObjectDisposedException) { }

            _chatWebView = null;
        }

        _isInitialized = false;
        _initializing = false;
        _hasRenderedRestored = false;
        _titleGenerated = false;

        // Reset layout to empty state so recycled container starts clean
        if (_chatLayout?.Visibility == Visibility.Visible)
        {
            _chatLayout.Children.Remove(_inputArea);
            if (_inputArea is not null)
                _inputArea.HorizontalAlignment = HorizontalAlignment.Stretch;
            if (_inputArea is not null)
                _emptyStateContent?.Children.Add(_inputArea);
            _chatLayout.Visibility = Visibility.Collapsed;
            if (_emptyStatePanel is not null)
                _emptyStatePanel.Visibility = Visibility.Visible;
        }

        DiagnosticLogger.LogInfo("[Chat] Disposed: WebView2 closed and removed from visual tree");
    }

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
