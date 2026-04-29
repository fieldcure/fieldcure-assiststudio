using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls.Helpers;
using FieldCure.AssistStudio.Core.Helpers;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections;
using Windows.ApplicationModel.DataTransfer;

namespace FieldCure.AssistStudio.Controls;

public sealed partial class ChatPanel
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Provider"/> dependency property.</summary>
    public static readonly DependencyProperty ProviderProperty =
        DependencyProperty.Register(nameof(Provider), typeof(IAiProvider), typeof(ChatPanel),
            new PropertyMetadata(null, OnProviderChanged));

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

    /// <summary>Identifies the <see cref="AvailableModels"/> dependency property.</summary>
    public static readonly DependencyProperty AvailableModelsProperty =
        DependencyProperty.Register(nameof(AvailableModels), typeof(IList), typeof(ChatPanel),
            new PropertyMetadata(null, OnAvailableModelsChanged));

    /// <summary>Identifies the <see cref="SelectedModel"/> dependency property.</summary>
    public static readonly DependencyProperty SelectedModelProperty =
        DependencyProperty.Register(nameof(SelectedModel), typeof(ProviderModel), typeof(ChatPanel),
            new PropertyMetadata(null, OnSelectedModelChanged));

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

    /// <summary>Identifies the <see cref="DisableInternalSendFlow"/> dependency property.</summary>
    public static readonly DependencyProperty DisableInternalSendFlowProperty =
        DependencyProperty.Register(nameof(DisableInternalSendFlow), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="RecentTurnsToKeep"/> dependency property.</summary>
    public static readonly DependencyProperty RecentTurnsToKeepProperty =
        DependencyProperty.Register(nameof(RecentTurnsToKeep), typeof(int), typeof(ChatPanel),
            new PropertyMetadata(10));

    /// <summary>Identifies the <see cref="AuxiliaryProviderResolver"/> dependency property.</summary>
    public static readonly DependencyProperty AuxiliaryProviderResolverProperty =
        DependencyProperty.Register(nameof(AuxiliaryProviderResolver), typeof(IAuxiliaryProviderResolver), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="TitleModel"/> dependency property.</summary>
    public static readonly DependencyProperty TitleModelProperty =
        DependencyProperty.Register(nameof(TitleModel), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null));

    /// <summary>Identifies the <see cref="SummaryModel"/> dependency property.</summary>
    public static readonly DependencyProperty SummaryModelProperty =
        DependencyProperty.Register(nameof(SummaryModel), typeof(string), typeof(ChatPanel),
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

    /// <summary>Identifies the <see cref="ShowModelSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ShowModelSelectorProperty =
        DependencyProperty.Register(nameof(ShowModelSelector), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(true, OnShowModelSelectorChanged));

    /// <summary>Identifies the <see cref="ShowProfileSelector"/> dependency property.</summary>
    public static readonly DependencyProperty ShowProfileSelectorProperty =
        DependencyProperty.Register(nameof(ShowProfileSelector), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(true, OnShowProfileSelectorChanged));

    /// <summary>Identifies the <see cref="WorkspaceFolders"/> dependency property.</summary>
    public static readonly DependencyProperty WorkspaceFoldersProperty =
        DependencyProperty.Register(nameof(WorkspaceFolders), typeof(IReadOnlyList<string>), typeof(ChatPanel),
            new PropertyMetadata(null, OnWorkspaceFoldersChanged));

    /// <summary>Identifies the <see cref="IsWorkspaceEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsWorkspaceEnabledProperty =
        DependencyProperty.Register(nameof(IsWorkspaceEnabled), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(true, OnIsWorkspaceEnabledChanged));

    /// <summary>Identifies the <see cref="KnowledgeBaseId"/> dependency property.</summary>
    public static readonly DependencyProperty KnowledgeBaseIdProperty =
        DependencyProperty.Register(nameof(KnowledgeBaseId), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(null, OnKnowledgeBaseIdChanged));

    private static void OnKnowledgeBaseIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel)
            panel.UpdateFolderButtonBadge();
    }

    /// <summary>Identifies the <see cref="IsKnowledgeBaseEnabled"/> dependency property.</summary>
    public static readonly DependencyProperty IsKnowledgeBaseEnabledProperty =
        DependencyProperty.Register(nameof(IsKnowledgeBaseEnabled), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="IsKbIndexing"/> dependency property.</summary>
    public static readonly DependencyProperty IsKbIndexingProperty =
        DependencyProperty.Register(nameof(IsKbIndexing), typeof(bool), typeof(ChatPanel),
            new PropertyMetadata(false));

    /// <summary>Identifies the <see cref="KbIndexingProgress"/> dependency property.</summary>
    public static readonly DependencyProperty KbIndexingProgressProperty =
        DependencyProperty.Register(nameof(KbIndexingProgress), typeof(double), typeof(ChatPanel),
            new PropertyMetadata(0.0));

    /// <summary>Identifies the <see cref="KbIndexingText"/> dependency property.</summary>
    public static readonly DependencyProperty KbIndexingTextProperty =
        DependencyProperty.Register(nameof(KbIndexingText), typeof(string), typeof(ChatPanel),
            new PropertyMetadata(""));

    /// <summary>Identifies the <see cref="IsKbLocked"/> dependency property.</summary>
    public static readonly DependencyProperty IsKbLockedProperty =
        DependencyProperty.Register(nameof(IsKbLocked), typeof(bool), typeof(ChatPanel),
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
    /// Called when <see cref="Provider"/> changes to push audio capability metadata to the input area.
    /// Drives ComposeBar's send-time audio reject classification.
    /// </summary>
    private static void OnProviderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatPanel panel || panel._inputArea is null) return;
        var provider = e.NewValue as IAiProvider;
        panel._inputArea.AudioCapability = provider?.AudioCapability ?? AudioCapability.NotSupported;
        panel._inputArea.AudioProviderName = provider?.ProviderName;
    }

    /// <summary>
    /// Called when <see cref="AvailableModels"/> changes to push preset list to the input area.
    /// </summary>
    private static void OnAvailableModelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.AvailableModels = e.NewValue as IList;
        }
    }

    /// <summary>
    /// Called when <see cref="SelectedModel"/> changes to sync the input area and update placeholder text.
    /// </summary>
    private static void OnSelectedModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChatPanel panel) return;

        if (e.NewValue is ProviderModel preset)
        {
            if (panel._inputArea is not null)
                panel._inputArea.SelectedModel = preset;
            var displayName = preset.ProviderType == "Mock" ? "Demo" : preset.Name;
            var label = string.IsNullOrEmpty(preset.ModelId)
                ? displayName
                : $"{displayName}/{preset.ModelId}";
            panel.UpdatePlaceholderWithProvider(label);
        }
        else
        {
            // Preset cleared (all providers removed)
            if (panel._inputArea is not null)
                panel._inputArea.SelectedModel = null;
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
            panel._renderer.WorkspaceFolders = e.NewValue as IReadOnlyList<string>;
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

    /// <summary>
    /// Called when <see cref="ShowModelSelector"/> changes to show or hide the preset ComboBox.
    /// </summary>
    private static void OnShowModelSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.ShowModelSelector = (bool)e.NewValue;
        }
    }

    /// <summary>
    /// Called when <see cref="ShowProfileSelector"/> changes to show or hide the profile ComboBox.
    /// </summary>
    private static void OnShowProfileSelectorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ChatPanel panel && panel._inputArea is not null)
        {
            panel._inputArea.ShowProfileSelector = (bool)e.NewValue;
        }
    }

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
    /// Gets or sets whether to disable the internal AI provider send flow.
    /// When <c>true</c>, <see cref="UserMessageSubmitted"/> fires but the built-in provider pipeline is skipped.
    /// Use this when driving the conversation externally (e.g., via Anthropic SDK directly).
    /// </summary>
    public bool DisableInternalSendFlow
    {
        get => (bool)GetValue(DisableInternalSendFlowProperty);
        set => SetValue(DisableInternalSendFlowProperty, value);
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
    /// Gets or sets the resolver for auxiliary providers (title, summary).
    /// When set, resolves the requested preset with automatic fallback to the main <see cref="Provider"/>.
    /// </summary>
    public IAuxiliaryProviderResolver? AuxiliaryProviderResolver
    {
        get => (IAuxiliaryProviderResolver?)GetValue(AuxiliaryProviderResolverProperty);
        set => SetValue(AuxiliaryProviderResolverProperty, value);
    }

    /// <summary>
    /// Gets or sets the model name for title generation.
    /// <see langword="null"/> or empty means inherit from the current conversation provider.
    /// </summary>
    public string? TitleModel
    {
        get => (string?)GetValue(TitleModelProperty);
        set => SetValue(TitleModelProperty, value);
    }

    /// <summary>
    /// Gets or sets the model name for summary generation.
    /// <see langword="null"/> or empty means inherit from the current conversation provider.
    /// </summary>
    public string? SummaryModel
    {
        get => (string?)GetValue(SummaryModelProperty);
        set => SetValue(SummaryModelProperty, value);
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
    /// Gets or sets whether the preset (model) selector is visible in the compose bar.
    /// </summary>
    public bool ShowModelSelector
    {
        get => (bool)GetValue(ShowModelSelectorProperty);
        set => SetValue(ShowModelSelectorProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the profile selector is visible in the compose bar.
    /// </summary>
    public bool ShowProfileSelector
    {
        get => (bool)GetValue(ShowProfileSelectorProperty);
        set => SetValue(ShowProfileSelectorProperty, value);
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
    /// Gets or sets the Knowledge Base folder path for the current conversation.
    /// Single folder — each conversation has at most one knowledge base.
    /// </summary>
    public string? KnowledgeBaseId
    {
        get => (string?)GetValue(KnowledgeBaseIdProperty);
        set => SetValue(KnowledgeBaseIdProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Knowledge Base capability is enabled in the current profile.
    /// </summary>
    public bool IsKnowledgeBaseEnabled
    {
        get => (bool)GetValue(IsKnowledgeBaseEnabledProperty);
        set => SetValue(IsKnowledgeBaseEnabledProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Knowledge Base is currently indexing.
    /// Controls visibility of the progress ring in the title bar and progress bar in the flyout.
    /// </summary>
    public bool IsKbIndexing
    {
        get => (bool)GetValue(IsKbIndexingProperty);
        set => SetValue(IsKbIndexingProperty, value);
    }

    /// <summary>
    /// Gets or sets the indexing progress as a percentage (0–100).
    /// </summary>
    public double KbIndexingProgress
    {
        get => (double)GetValue(KbIndexingProgressProperty);
        set => SetValue(KbIndexingProgressProperty, value);
    }

    /// <summary>
    /// Gets or sets the indexing status text (e.g., "3/10 files...").
    /// </summary>
    public string KbIndexingText
    {
        get => (string)GetValue(KbIndexingTextProperty);
        set => SetValue(KbIndexingTextProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the Knowledge Base folder is locked by another process.
    /// When true, shows a lock icon and hides the reindex button.
    /// </summary>
    public bool IsKbLocked
    {
        get => (bool)GetValue(IsKbLockedProperty);
        set => SetValue(IsKbLockedProperty, value);
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
    public IList? AvailableModels
    {
        get => (IList?)GetValue(AvailableModelsProperty);
        set => SetValue(AvailableModelsProperty, value);
    }

    /// <summary>
    /// Gets or sets the currently selected provider preset.
    /// </summary>
    public ProviderModel? SelectedModel
    {
        get => (ProviderModel?)GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
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
    public event EventHandler<ProviderModel>? ModelChanged;

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
    /// Occurs when the user sets or removes the Knowledge Base folder via the flyout.
    /// The event argument is the folder path (null to remove).
    /// </summary>
    public event EventHandler<string?>? KnowledgeBaseIdChanged;

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

    /// <summary>
    /// Occurs when the user submits a message via the compose bar, after the user message
    /// has been added to the conversation. When <see cref="DisableInternalSendFlow"/> is <c>true</c>,
    /// the internal provider send flow is skipped and external code is expected to drive the assistant response.
    /// </summary>
    public event EventHandler<MessageSentEventArgs>? UserMessageSubmitted;

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
            _inputArea.ModelChanged -= OnInputModelChanged;
            _inputArea.ProfileChanged -= OnInputProfileChanged;
            _inputArea.StopRequested -= OnStopRequested;
            _inputArea.EditCanceled -= OnComposeBarEditCanceled;
        }
        if (_approvalPanel is not null)
        {
            _approvalPanel.Approved -= OnToolApproved;
            _approvalPanel.Rejected -= OnToolRejected;
        }
        if (_elicitationPanel is not null)
        {
            _elicitationPanel.Submitted -= OnElicitationSubmitted;
            _elicitationPanel.Declined -= OnElicitationDeclined;
            _elicitationPanel.Cancelled -= OnElicitationCancelled;
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
        _kbDisabledHint = null;
        _kbSelector = null;
        _kbEmpty = null;

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
        _elicitationPanel = GetTemplateChild("PART_ToolElicitationPanel") as ToolElicitationPanel;

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
        if (_searchPrevButton is not null)
        {
            _searchPrevButton.Click += (s, e) => _ = NavigateSearchAsync(-1);
            var tip = SafeGetResString("Chat_SearchPrevTooltip") ?? "Previous";
            SetBottomRightToolTip(_searchPrevButton, tip);
            AutomationHelper.SetAutomation(_searchPrevButton, "ChatPanelSearchPrevButton",
                nameKey: "Chat_SearchPrevName");
        }
        if (_searchNextButton is not null)
        {
            _searchNextButton.Click += (s, e) => _ = NavigateSearchAsync(1);
            var tip = SafeGetResString("Chat_SearchNextTooltip") ?? "Next";
            SetBottomRightToolTip(_searchNextButton, tip);
            AutomationHelper.SetAutomation(_searchNextButton, "ChatPanelSearchNextButton",
                nameKey: "Chat_SearchNextName");
        }
        if (_searchCloseButton is not null)
        {
            _searchCloseButton.Click += (s, e) => CloseSearchBar();
            var tip = SafeGetResString("Chat_SearchCloseTooltip") ?? "Close search";
            SetBottomRightToolTip(_searchCloseButton, tip);
            AutomationHelper.SetAutomation(_searchCloseButton, "ChatPanelSearchCloseButton",
                nameKey: "Chat_SearchCloseName");
        }

        // Wire approval panel events
        if (_approvalPanel is not null)
        {
            _approvalPanel.Approved += OnToolApproved;
            _approvalPanel.Rejected += OnToolRejected;
        }

        // Wire elicitation panel events
        if (_elicitationPanel is not null)
        {
            _elicitationPanel.Submitted += OnElicitationSubmitted;
            _elicitationPanel.Declined += OnElicitationDeclined;
            _elicitationPanel.Cancelled += OnElicitationCancelled;
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
            _inputArea.MessageSent += OnMessageSent;
            _inputArea.ModelChanged += OnInputModelChanged;
            _inputArea.ProfileChanged += OnInputProfileChanged;
            _inputArea.StopRequested += OnStopRequested;
            _inputArea.EditCanceled += OnComposeBarEditCanceled;
            // Push current values (may have been set before template was applied)
            if (AvailableModels is { } presets)
                _inputArea.AvailableModels = presets;
            if (SelectedModel is { } selectedPreset)
                _inputArea.SelectedModel = selectedPreset;
            if (AvailableProfiles is { } promptPresets)
                _inputArea.AvailableProfiles = promptPresets;
            if (SelectedProfile is { } selectedProfile)
            {
                _inputArea.SelectedProfile = selectedProfile;
                _inputArea.SelectProfileInCombo(selectedProfile);
            }
            // Push audio capability (Provider may have been set before template was applied)
            if (Provider is { } currentProvider)
            {
                _inputArea.AudioCapability = currentProvider.AudioCapability;
                _inputArea.AudioProviderName = currentProvider.ProviderName;
            }

            // Sync tools and visibility settings
            _inputArea.AvailableTools = RegisteredTools;
            _inputArea.ShowAttachButton = AllowAttachments;
            _inputArea.ShowModelSelector = ShowModelSelector;
            _inputArea.ShowProfileSelector = ShowProfileSelector;
            if (IsReadOnly)
                _inputArea.Visibility = Visibility.Collapsed;
        }
        if (_rootGrid is not null)
        {
            _rootGrid.DragOver += OnDragOver;
            _rootGrid.Drop += OnDrop;
        }
        // Apply initial ShowTitleBar value (XAML may set False before OnApplyTemplate)
        if (_titleBar is not null && !ShowTitleBar)
            _titleBar.Visibility = Visibility.Collapsed;

        if (_titleEditButton is not null)
        {
            _titleEditButton.Click += OnTitleEditClick;
            var tooltip = SafeGetResString("ChatPanel_EditTitleTooltip");
            SetBottomRightToolTip(_titleEditButton, !string.IsNullOrEmpty(tooltip) ? tooltip : "Edit title");
            AutomationHelper.SetAutomation(_titleEditButton, "ChatPanelTitleEditButton",
                nameKey: "ChatPanel_EditTitleName");
        }
        if (_titleRefreshButton is not null)
        {
            _titleRefreshButton.Click += OnTitleRefreshClick;
            AutomationHelper.SetAutomation(_titleRefreshButton, "ChatPanelTitleRefreshButton",
                nameKey: "ChatPanel_RefreshTitleName");
        }
        if (_titleFolderButton is not null)
        {
            // Wire Flyout.Opening for lazy PART_ resolution and content population
            if (_titleFolderButton.Flyout is Flyout folderFlyout)
            {
                folderFlyout.Opened += OnFolderFlyoutOpened;
                folderFlyout.Opening += OnFolderFlyoutOpening;
            }

            SetBottomRightToolTip(_titleFolderButton, SafeGetResString("Folder_Tooltip") ?? "Folders");
            AutomationHelper.SetAutomation(_titleFolderButton, "ChatPanelFolderButton",
                nameKey: "ChatPanel_TitleFolderName");
        }

        UpdateFolderButtonBadge();
        UpdateFolderButtonAppearance();
        UpdateTitleDisplay();

        // Subscribe to Loaded for WebView2 initialization
        Loaded += OnLoaded;
    }

    #endregion

    #region Loaded Handler

    /// <summary>
    /// Handles the Loaded event to initialize the WebView2 renderer and render any pre-existing messages.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        DiagnosticLogger.LogInfo($"[Chat] OnLoaded: initialized={_isInitialized}, initializing={_initializing}, webView={_chatWebView is not null}");
        if (_isInitialized || _initializing) return;

        try
        {
            if (_chatWebView is null)
            {
                DiagnosticLogger.LogInfo("[Chat] OnLoaded: _chatWebView is null, skipping init (disposed panel?)");
                return;
            }

            _initializing = true;
            await _renderer.InitializeAsync(_chatWebView);
            _isInitialized = true;
            _needsWebViewReinitialization = false;
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
            _initializing = false;
            DiagnosticLogger.LogException(ex);
        }
    }

    #endregion

    #region Drag & Drop

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

    #region Input Event Handlers

    /// <summary>
    /// Handles the preset changed event from the input area to propagate the selection.
    /// </summary>
    private void OnInputModelChanged(object? sender, ProviderModel preset)
    {
        SelectedModel = preset;
        ModelChanged?.Invoke(this, preset);
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

    #endregion

    #region Theme & Locale

    /// <summary>
    /// Determines whether the current theme is dark based on the <see cref="Theme"/> property or system setting.
    /// </summary>
    private bool IsDarkTheme() => Theme switch
    {
        ChatTheme.Light => false,
        ChatTheme.Dark => true,
        _ => ActualTheme == ElementTheme.Dark
    };

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
            var strings = new Dictionary<string, string>
            {
                ["copy"] = Res.GetString("Chat_Copy"),
                ["copied"] = Res.GetString("Chat_Copied"),
                ["continue_label"] = Res.GetString("Chat_Continue"),
                ["code"] = Res.GetString("Chat_Code"),
                ["copyPrompt"] = Res.GetString("Chat_CopyPrompt"),
                ["copyMessage"] = Res.GetString("Chat_CopyMessage"),
                ["edit"] = Res.GetString("Chat_Edit"),
                ["retry"] = Res.GetString("Chat_Retry"),
                ["copyRequest"] = Res.GetString("Chat_CopyRequest"),
                ["copyResponse"] = Res.GetString("Chat_CopyResponse"),
                ["tokens"] = Res.GetString("Chat_Tokens"),
                ["editBranchHint"] = Res.GetString("Chat_EditBranchHint"),
                ["editCancel"] = Res.GetString("Chat_EditCancel"),
                ["editSave"] = Res.GetString("Chat_EditSave"),
                ["showMore"] = Res.GetString("Chat_ShowMore"),
                ["showLess"] = Res.GetString("Chat_ShowLess"),
                ["imageSave"] = Res.GetString("Chat_ImageSave"),
                ["imageCopy"] = Res.GetString("Chat_ImageCopy"),
                ["imageSaveLabel"] = Res.GetString("Chat_ImageSaveLabel"),
                ["imageSaved"] = Res.GetString("Chat_ImageSaved"),
                ["imageCopied"] = Res.GetString("Chat_ImageCopied"),
                ["seconds"] = Res.GetString("Chat_Seconds"),
                ["minutes"] = Res.GetString("Chat_Minutes"),
                ["hours"] = Res.GetString("Chat_Hours"),
                ["summaryHeader"] = Res.GetString("Chat_SummaryHeader"),
                ["diagramSaveSvg"] = Res.GetString("Chat_DiagramSaveSvg"),
                ["diagramSavePng"] = Res.GetString("Chat_DiagramSavePng"),
                ["diagramCopyLabel"] = Res.GetString("Chat_DiagramCopyLabel"),
                ["diagramCopyTooltip"] = Res.GetString("Chat_DiagramCopyTooltip")
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

    #endregion

    #region UI Utilities

    /// <summary>
    /// Resolves a resource string from the Controls <c>.resw</c> map, returning <c>null</c>
    /// when the key or resource map cannot be found rather than propagating the COMException.
    /// Use this for resource lookups that happen during template or layout passes where an
    /// unhandled exception would tear down the control tree.
    /// </summary>
    private static string? SafeGetResString(string key)
    {
        try
        {
            var value = Res.GetString(key);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Wraps <paramref name="text"/> in a <see cref="ToolTip"/> with
    /// <see cref="Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse"/> placement
    /// and attaches it to <paramref name="element"/>. All tooltips in this library follow
    /// the project-wide Mouse-placement UX convention; use this helper instead of the
    /// plain <c>ToolTipService.SetToolTip(element, string)</c> form to avoid tooltips
    /// defaulting to the Top/Bottom placement.
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
        var providerName = Provider?.ProviderName ?? "";

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

    #endregion
}
