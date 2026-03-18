using System.Collections;
using System.Collections.ObjectModel;
using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.AssistStudio.Providers;
using FieldCure.AssistStudio.Rendering;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

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
public sealed class ChatPanel : Control
{
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
            var title = e.NewValue as string;
            var hasTitle = !string.IsNullOrEmpty(title);
            if (panel._titleText is not null)
                panel._titleText.Text = title ?? "";
            if (panel._titleBar is not null)
                panel._titleBar.Visibility = hasTitle
                    ? Microsoft.UI.Xaml.Visibility.Visible
                    : Microsoft.UI.Xaml.Visibility.Collapsed;

            if (hasTitle)
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
            panel.SystemPrompt = preset.Text;
        }
    }

    #endregion

    #region Fields

    /// <summary>
    /// The WebView-based chat renderer responsible for HTML rendering of messages.
    /// </summary>
    private readonly WebViewChatRenderer _renderer = new();

    /// <summary>
    /// The in-memory collection of all chat messages in the current conversation.
    /// </summary>
    private readonly ObservableCollection<ChatMessage> _messages = [];

    /// <summary>
    /// Cancellation token source for the currently active streaming operation.
    /// </summary>
    private CancellationTokenSource? _streamingCts;

    /// <summary>
    /// Whether the WebView2 renderer has been initialized.
    /// </summary>
    private bool _isInitialized;

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
    private InputContainer? _inputArea;

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

    /// <summary>
    /// The WebView2 control used to render chat messages as HTML.
    /// </summary>
    private WebView2? _chatWebView;

    /// <summary>
    /// The panel shown in place of InputContainer when a tool requires user confirmation.
    /// </summary>
    private ToolApprovalPanel? _approvalPanel;

    /// <summary>
    /// Completion source for awaiting user approval/rejection of a tool call.
    /// </summary>
    private TaskCompletionSource<bool>? _approvalTcs;

    #endregion

    #region Public Properties

    /// <summary>
    /// Maximum input tokens before auto-summarization triggers. 0 = disabled (default).
    /// </summary>
    public int MaxInputTokens { get; set; } = 0;

    /// <summary>
    /// Number of recent conversation turns to keep when summarizing.
    /// </summary>
    public int RecentTurnsToKeep { get; set; } = 10;

    /// <summary>
    /// Enable automatic conversation summarization when input tokens exceed MaxInputTokens.
    /// </summary>
    public bool AutoSummarize { get; set; } = false;

    /// <summary>
    /// Provider used for utility tasks (title generation, summarization).
    /// Falls back to the main Provider if not set.
    /// </summary>
    public IAiProvider? UtilityProvider { get; set; }

    /// <summary>
    /// Enable automatic title generation after the first assistant response.
    /// </summary>
    public bool AutoTitle { get; set; } = false;

    /// <summary>
    /// Registered tools available for AI tool calling. When non-empty, the provider uses
    /// CompleteAsync (non-streaming) to enable tool call responses.
    /// </summary>
    public IReadOnlyList<IAssistTool> RegisteredTools { get; set; } = [];

    /// <summary>
    /// Optional workspace context provider. When set, the current workspace state
    /// is automatically injected into every AI request.
    /// </summary>
    public IWorkspaceContext? WorkspaceContext { get; set; }

    /// <summary>
    /// Optional RAG context provider. When set, relevant context chunks are retrieved
    /// for the user's query and passed to the AI provider.
    /// </summary>
    public IContextProvider? ContextProvider { get; set; }

    /// <summary>
    /// Maximum number of consecutive tool call rounds before forcing a text response. Default is 10.
    /// </summary>
    public int MaxToolCallRounds { get; set; } = 10;

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
    /// Occurs when a keyboard shortcut is pressed inside the WebView2 that should be handled by the host.
    /// </summary>
    public event EventHandler<string>? KeyboardShortcutPressed;

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
        _renderer.SummarizeRequested += OnSummarizeRequested;
        _renderer.KeyboardShortcutPressed += (_, shortcut) => KeyboardShortcutPressed?.Invoke(this, shortcut);
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

        // Get template parts
        _rootGrid = GetTemplateChild("PART_RootGrid") as Grid;
        _emptyStatePanel = GetTemplateChild("PART_EmptyStatePanel") as Grid;
        _emptyStateContent = GetTemplateChild("PART_EmptyStateContent") as StackPanel;
        _inputArea = GetTemplateChild("PART_InputArea") as InputContainer;
        _chatLayout = GetTemplateChild("PART_ChatLayout") as Grid;
        _titleBar = GetTemplateChild("PART_TitleBar") as StackPanel;
        _titleText = GetTemplateChild("PART_TitleText") as TextBlock;
        _titleEditButton = GetTemplateChild("PART_TitleEditButton") as Button;
        _titleRefreshButton = GetTemplateChild("PART_TitleRefreshButton") as Button;
        _chatWebView = GetTemplateChild("PART_ChatWebView") as WebView2;
        _approvalPanel = GetTemplateChild("PART_ToolApprovalPanel") as ToolApprovalPanel;

        // Wire approval panel events
        if (_approvalPanel is not null)
        {
            _approvalPanel.Approved += OnToolApproved;
            _approvalPanel.Rejected += OnToolRejected;
        }

        // Set initial background to match CSS --bg-primary (before WebView2 loads)
        if (_rootGrid is not null)
            _rootGrid.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(LightBg);

        // Attach event handlers and sync current property values
        if (_inputArea is not null)
        {
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
        }
        if (_rootGrid is not null)
        {
            _rootGrid.DragOver += OnDragOver;
            _rootGrid.Drop += OnDrop;
        }
        if (_titleEditButton is not null)
        {
            _titleEditButton.Click += OnTitleEditClick;
            try
            {
                var loader = new Windows.ApplicationModel.Resources.ResourceLoader("AssistStudio.Controls/Resources");
                var tooltip = loader.GetString("ChatPanel_EditTitleTooltip");
                SetBottomRightToolTip(_titleEditButton, !string.IsNullOrEmpty(tooltip) ? tooltip : "Edit title");
            }
            catch
            {
                SetBottomRightToolTip(_titleEditButton, "Edit title");
            }
        }
        if (_titleRefreshButton is not null)
            _titleRefreshButton.Click += OnTitleRefreshClick;

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
    /// Adds a previously saved message to the conversation (for restoring saved conversations).
    /// Messages added before the WebView is initialized will be rendered once initialization completes.
    /// </summary>
    public void AddRestoredMessage(ChatRole role, string content,
        string? providerName = null, string? providerModelId = null)
    {
        var msg = new ChatMessage(role, content)
        {
            ProviderName = providerName,
            ProviderModelId = providerModelId,
        };
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
            if (IsDebugMode)
                await _renderer.SetDebugModeAsync(true);

            // Render any pre-existing messages (restored conversations)
            if (_messages.Count > 0)
            {
                SwitchToChatLayout();
            }
            foreach (var msg in _messages)
            {
                if (msg.Role == ChatRole.User)
                {
                    await _renderer.AppendUserMessageAsync(
                        msg.Id, msg.Content, msg.Timestamp.ToString("O"), msg.Attachments);
                }
                else if (msg.Role == ChatRole.Assistant)
                {
                    await _renderer.BeginAssistantMessageAsync(
                        msg.Id, msg.ProviderName, msg.ProviderModelId);
                    await _renderer.FinalizeMessageAsync(msg.Id, msg.Content);
                }
            }

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
        var userMessage = new ChatMessage(ChatRole.User, e.Text) { Attachments = e.Attachments };
        _messages.Add(userMessage);
        await _renderer.AppendUserMessageAsync(
            userMessage.Id, userMessage.Content, userMessage.Timestamp.ToString("O"),
            userMessage.Attachments);
        MessageAdded?.Invoke(this, userMessage);

        // Stream assistant response
        if (Provider is null)
        {
            System.Diagnostics.Debug.WriteLine("ChatPanel: Provider is null when sending message");
            var errorMsg = new ChatMessage(ChatRole.Assistant) { Content = "[Error: No AI provider configured]" };
            _messages.Add(errorMsg);
            await _renderer.BeginAssistantMessageAsync(errorMsg.Id, "Error", null);
            await _renderer.FinalizeMessageAsync(errorMsg.Id, errorMsg.Content);
            return;
        }

        var assistantMessage = new ChatMessage(ChatRole.Assistant)
        {
            IsStreaming = true,
            ProviderName = Provider.ProviderName,
            ProviderModelId = Provider.ModelId
        };
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
                _inputArea.IsInputEnabled = true;
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

        // Find the assistant message to continue
        var assistantMessage = _messages.LastOrDefault(m =>
            m.Role == ChatRole.Assistant && m.Id == messageId);
        if (assistantMessage is null) return;

        // Add a user message asking to continue (not shown in UI)
        var continueMessage = new ChatMessage(ChatRole.User, "Continue writing from where you left off.");
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
            var request = await CreateRequestAsync(_messages.ToList());

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
                _inputArea.IsInputEnabled = true;
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

        var userMessage = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Id == e.MessageId);
        if (userMessage is null) return;

        // Update message content
        userMessage.Content = e.NewText;

        // Remove everything after this user message
        var idx = _messages.IndexOf(userMessage);
        if (idx < 0) return;
        while (_messages.Count > idx + 1)
        {
            _messages.RemoveAt(_messages.Count - 1);
        }
        await _renderer.RemoveMessagesAfterAsync(e.MessageId);

        // Update the user bubble text in the UI
        if (_chatWebView is not null)
        {
            var escaped = System.Text.Json.JsonSerializer.Serialize(e.NewText);
            await _chatWebView.ExecuteScriptAsync(
                $"document.querySelector('#msg-{e.MessageId} .message-bubble').textContent = {escaped}");
        }

        // Re-send
        await StreamAssistantResponseAsync(userMessage);
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
                _inputArea.IsInputEnabled = true;
            UpdateSummarizeButtonState();
        }
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
                _inputArea.IsInputEnabled = true;
            UpdateSummarizeButtonState();
        }
    }

    /// <summary>
    /// Handles the prompt preset changed event from the input area to update the system prompt.
    /// </summary>
    private void OnInputProfileChanged(object? sender, Profile profile)
    {
        SelectedProfile = profile;
        SystemPrompt = profile.Text;
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

    #region Private Methods

    private record StreamResult(TokenUsage? Usage, bool IsTruncated, IReadOnlyList<ToolCall>? ToolCalls = null)
    {
        public bool HasToolCalls => ToolCalls is { Count: > 0 };
    }

    /// <summary>
    /// Consumes a stream of <see cref="StreamEvent"/> instances, appending text deltas to the message
    /// and rendering them in the chat UI. Returns aggregated usage, truncation info, and any tool calls.
    /// </summary>
    private async Task<StreamResult> ConsumeStreamAsync(
        IAsyncEnumerable<StreamEvent> events, ChatMessage message, CancellationToken ct)
    {
        TokenUsage? usage = null;
        var isTruncated = false;
        var toolAccumulator = new StreamToolCallAccumulator();
        var thinkingBlockStarted = false;

        await foreach (var evt in events.WithCancellation(ct))
        {
            switch (evt)
            {
                case StreamEvent.ThinkingDelta thinking:
                    if (!thinkingBlockStarted)
                    {
                        await _renderer.BeginThinkingBlockAsync(message.Id);
                        thinkingBlockStarted = true;
                    }
                    message.ThinkingContent = (message.ThinkingContent ?? "") + thinking.Text;
                    await _renderer.AppendThinkingTokenAsync(message.Id, thinking.Text);
                    break;
                case StreamEvent.TextDelta delta:
                    message.Content += delta.Text;
                    await _renderer.AppendTokenAsync(message.Id, delta.Text);
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
        }

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
        if (RegisteredTools.Count > 0)
        {
            executor = new ToolCallExecutor(RegisteredTools);
            if (_approvalPanel is not null && _inputArea is not null)
            {
                executor.ConfirmationHandler = async (toolName, arguments) =>
                {
                    _approvalPanel.ToolName = toolName;
                    _approvalPanel.ToolDisplayName = GetLocalizedToolName(toolName);
                    _approvalPanel.Arguments = arguments;
                    _approvalPanel.IsExpanded = false;
                    _inputArea.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _approvalPanel.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                    _approvalTcs = new TaskCompletionSource<bool>();
                    var approved = await _approvalTcs.Task;

                    _approvalPanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
                    _inputArea.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                    return approved;
                };
            }
        }

        StreamResult result;
        var round = 0;

        do
        {
            var request = await CreateRequestAsync(_messages.ToList());
            result = await ConsumeStreamAsync(Provider!.StreamAsync(request, ct), assistantMessage, ct);

            if (!result.HasToolCalls || executor is null)
                break;

            round++;
            if (round > MaxToolCallRounds) break;

            // Add the assistant's tool call message to history
            var toolCallMsg = new ChatMessage(ChatRole.Assistant)
            {
                ToolCalls = result.ToolCalls,
                Content = assistantMessage.Content,
                ProviderName = Provider.ProviderName,
                ProviderModelId = Provider.ModelId
            };
            _messages.Add(toolCallMsg);

            // Execute each tool call and add results
            foreach (var call in result.ToolCalls!)
            {
                string toolResult;
                try
                {
                    toolResult = await executor.ExecuteAsync(call, ct);
                }
                catch (Exception ex)
                {
                    DiagnosticLogger.LogException(ex);
                    toolResult = $"{{\"error\":\"{ex.Message}\"}}";
                }

                _messages.Add(new ChatMessage(ChatRole.Tool, toolResult) { ToolCallId = call.Id });

                var toolStatus = $"[Tool: {call.FunctionName}]\n";
                assistantMessage.Content += toolStatus;
                await _renderer.AppendTokenAsync(assistantMessage.Id, toolStatus);
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
    /// Transitions the layout from the empty state panel to the active chat layout.
    /// </summary>
    private void SwitchToChatLayout()
    {
        if (_chatLayout is null || _emptyStatePanel is null ||
            _emptyStateContent is null || _inputArea is null) return;
        if (_chatLayout.Visibility == Microsoft.UI.Xaml.Visibility.Visible) return;

        _emptyStatePanel.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
        _chatLayout.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

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
            ProviderModelId = Provider.ModelId
        };
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
                _inputArea.IsInputEnabled = true;
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
            try
            {
                var loader = new Windows.ApplicationModel.Resources.ResourceLoader("AssistStudio.Controls/Resources");
                var fallback = loader.GetString("InputContainer_Placeholder");
                Placeholder = !string.IsNullOrEmpty(fallback) ? fallback : "Type a message...";
            }
            catch { Placeholder = "Type a message..."; }
            return;
        }

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader("AssistStudio.Controls/Resources");
            var format = loader.GetString("InputContainer_AskProvider");
            if (!string.IsNullOrEmpty(format))
            {
                Placeholder = string.Format(format, providerName);
                return;
            }
        }
        catch { /* fallback */ }

        Placeholder = $"Ask {providerName}...";
    }

    /// <summary>
    /// Updates the title refresh button tooltip to include the provider name.
    /// </summary>
    private void UpdateRefreshTooltip()
    {
        var provider = UtilityProvider ?? Provider;
        var providerName = provider?.ProviderName ?? "";

        try
        {
            var loader = new Windows.ApplicationModel.Resources.ResourceLoader("AssistStudio.Controls/Resources");
            var format = loader.GetString("Chat_RegenerateTitle");
            if (!string.IsNullOrEmpty(format) && !string.IsNullOrEmpty(providerName) && _titleRefreshButton is not null)
            {
                SetBottomRightToolTip(_titleRefreshButton, string.Format(format, providerName));
                return;
            }
        }
        catch { /* fallback */ }

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

            DispatcherQueue.TryEnqueue(() => TitleGenerated?.Invoke(this, title));
        }
        catch (Exception ex)
        {
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
                ["tokens"] = loader.GetString("Chat_Tokens")
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
    private void OnToolApproved(object? sender, EventArgs e) => _approvalTcs?.TrySetResult(true);

    /// <summary>
    /// Handles the Rejected event from the ToolApprovalPanel.
    /// </summary>
    private void OnToolRejected(object? sender, EventArgs e) => _approvalTcs?.TrySetResult(false);

    /// <summary>
    /// Returns a localized display name for a tool, falling back to the tool name.
    /// </summary>
    private string GetLocalizedToolName(string toolName)
    {
        var tool = RegisteredTools.FirstOrDefault(t => t.Name == toolName);
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
    private async Task<AiRequest> CreateRequestAsync(IReadOnlyList<ChatMessage> messages, string? systemPrompt = null)
    {
        var workspaceText = WorkspaceContext is not null
            ? await WorkspaceContext.GetContextAsync()
            : null;

        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User)?.Content;
        var chunks = ContextProvider is not null && lastUserMsg is not null
            ? await ContextProvider.RetrieveAsync(lastUserMsg)
            : null;

        var preset = SelectedPreset;
        return new AiRequest
        {
            Messages = messages,
            SystemPrompt = systemPrompt ?? SystemPrompt,
            WorkspaceText = workspaceText,
            ContextChunks = chunks is { Count: > 0 } ? chunks : null,
            Temperature = preset?.Temperature ?? 0.7,
            MaxTokens = preset?.MaxTokens ?? 4096,
            Tools = RegisteredTools.Count > 0 ? RegisteredTools : null
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
}
