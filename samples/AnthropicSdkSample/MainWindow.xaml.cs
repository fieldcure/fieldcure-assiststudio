using Anthropic;
using AnthropicSdkSample.Controls;
using AnthropicSdkSample.Tools;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Controls.Anthropic;
using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using Windows.ApplicationModel.DataTransfer;
using Windows.Security.Credentials;
using Windows.Storage;
using Windows.UI;

namespace AnthropicSdkSample;

/// <summary>
/// Demonstrates ChatPanel integration with the Anthropic C# SDK.
/// On first launch, prompts for an API key and stores it securely in Windows Credential Manager.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Reference to the underlying app window for caption button theming.</summary>
    private readonly AppWindow? _appWindow;

    /// <summary>
    /// Fallback model IDs used only when the Anthropic <c>client.Models.List()</c> call
    /// fails (offline or transient error). Mirrors the legacy fallback shape, IDs only.
    /// </summary>
    private static readonly string[] FallbackModelIds =
    [
        "claude-opus-4-7",
        "claude-sonnet-4-6",
        "claude-haiku-4-5",
    ];

    /// <summary>LocalSettings key for the persisted model selection.</summary>
    private const string ModelSettingName = "SelectedModelId";

    /// <summary>LocalSettings key for the persisted app theme selection.</summary>
    private const string ThemeSettingName = "RequestedTheme";

    /// <summary>PasswordVault resource name for credential storage.</summary>
    private const string VaultResource = "AnthropicSdkSample";

    /// <summary>PasswordVault user name (single-key app, so a fixed name suffices).</summary>
    private const string VaultUser = "ApiKey";

    /// <summary>
    /// Anthropic SDK client. Initialized in the ctor when the Credential Manager has a
    /// stored API key (the common case after first run), otherwise in
    /// <see cref="OnContentLoaded"/> once the user enters one through the prompt.
    /// </summary>
    private AnthropicClient? _client;

    /// <summary>
    /// Tools exposed to the model on every turn. The sample ships a single fetch tool to
    /// demonstrate the adapter's tool plumbing without pulling in an external MCP server.
    /// Consumers normally build this list from their own <see cref="IAssistTool"/> implementations
    /// (or by adapting MCP server tools to <see cref="IAssistTool"/>) and pass it to
    /// <see cref="ChatPanelExtensions.BuildAnthropicParams"/> on each turn.
    /// </summary>
    private readonly IList<IAssistTool> _tools = [new FetchTool()];

    /// <summary>
    /// Hard ceiling on tool-result rounds within a single user turn — guards against a
    /// runaway loop if the model keeps calling tools without ever returning prose.
    /// </summary>
    private const int MaxToolRounds = 8;

    /// <summary>Initializes the window, sets up the custom title bar, and subscribes to events.</summary>
    public MainWindow()
    {
        InitializeComponent();

        // Register window for FileOpenPicker native interop
        WindowHelper.TrackWindow(this);

        // Custom title bar
        ExtendsContentIntoTitleBar = true;
        var titleBar = ((FrameworkElement)Content).FindName("AppTitleBar") as UIElement;
        if (titleBar is not null) SetTitleBar(titleBar);
        _appWindow = AppWindow;

        // Theme first; model list is populated once the API key / client is ready (see OnContentLoaded).
        InitializeTheme();

        ChatPanel.UserMessageSubmitted += OnUserMessageSubmitted;
        ChatPanel.ModelChanged += OnChatPanelModelChanged;

        // Vault hit is the common case. Constructing the client here (synchronously,
        // before the window is drawn) closes the Loaded-event race that previously
        // dropped an early "hi" message. The vault-miss path is handled in
        // OnContentLoaded, where ChatPanel sits behind a modal prompt dialog and the
        // user cannot send until the dialog returns — so no IsEnabled toggling is
        // required (an earlier attempt to gate ChatPanel.IsEnabled broke the WebView2
        // hosting layer and prevented send entirely).
        var apiKey = LoadApiKeyFromVault();
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _client = new AnthropicClient { ApiKey = apiKey };
        }

        ((FrameworkElement)Content).Loaded += OnContentLoaded;
    }

    #region Model Selector

    /// <summary>
    /// Fetches the live Claude model list from the Anthropic SDK and feeds it into
    /// <see cref="ChatPanel.AvailableModels"/>. Falls back to <see cref="FallbackModelIds"/>
    /// when the API call fails so the sample remains usable offline or during transient
    /// failures. The chat panel's built-in <see cref="ModelPicker"/> renders the list.
    /// </summary>
    /// <param name="apiKey">The current Anthropic API key (forwarded to each
    /// <see cref="ProviderModel"/> for completeness — unused while
    /// <see cref="ChatPanel.DisableInternalSendFlow"/> is true).</param>
    private async Task LoadModelsAsync(string apiKey)
    {
        if (_client is null) return;

        List<ProviderModel> claudeModels;
        try
        {
            var page = await _client.Models.List();
            claudeModels = page.Items
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => new ProviderModel
                {
                    Name = m.ID,
                    ProviderType = "Claude",
                    ModelId = m.ID,
                    ApiKey = apiKey,
                })
                .ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SdkSample] Models.List() failed; using fallback: {ex.Message}");
            claudeModels = [.. FallbackModelIds.Select(id => new ProviderModel
            {
                Name = id,
                ProviderType = "Claude",
                ModelId = id,
                ApiKey = apiKey,
            })];
        }

        var settings = ApplicationData.Current.LocalSettings;
        var savedModelId = settings.Values[ModelSettingName] as string;

        ChatPanel.AvailableModels = claudeModels;
        ChatPanel.SelectedModel = claudeModels.FirstOrDefault(m => m.ModelId == savedModelId)
                               ?? claudeModels.FirstOrDefault();
    }

    /// <summary>Persists the user's model selection from the ChatPanel's built-in picker.</summary>
    /// <param name="sender">The chat panel.</param>
    /// <param name="model">The newly selected model.</param>
    private void OnChatPanelModelChanged(object? sender, ProviderModel model)
    {
        if (model is null) return;
        var settings = ApplicationData.Current.LocalSettings;
        settings.Values[ModelSettingName] = model.ModelId;
    }

    #endregion

    #region Theme

    /// <summary>Restores the saved theme or uses the system theme by default.</summary>
    private void InitializeTheme()
    {
        var settings = ApplicationData.Current.LocalSettings;
        var savedTheme = settings.Values[ThemeSettingName] as string;

        var theme = savedTheme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light,
        };

        ApplyTheme(theme, persist: false);
    }

    /// <summary>Toggles between the light and dark themes.</summary>
    private void OnThemeToggleClick(object sender, RoutedEventArgs e)
    {
        var nextTheme = GetEffectiveTheme() == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;

        ApplyTheme(nextTheme, persist: true);
    }

    /// <summary>Applies the selected theme to the sample window and chat panel.</summary>
    private void ApplyTheme(ElementTheme theme, bool persist)
    {
        if (Content is FrameworkElement root)
            root.RequestedTheme = theme;

        ApplyTitleBarTheme(theme);
        ChatPanel.Theme = theme == ElementTheme.Dark ? ChatTheme.Dark : ChatTheme.Light;
        UpdateThemeButton(theme);

        if (!persist) return;

        var settings = ApplicationData.Current.LocalSettings;
        settings.Values[ThemeSettingName] = theme == ElementTheme.Dark ? "Dark" : "Light";
    }

    /// <summary>Updates the theme menu item label and icon to reflect the next available theme.</summary>
    private void UpdateThemeButton(ElementTheme theme)
    {
        var nextThemeIsDark = theme != ElementTheme.Dark;
        ThemeMenuItem.Text = nextThemeIsDark ? "Switch to dark" : "Switch to light";
        ThemeMenuItem.Icon = new FontIcon
        {
            Glyph = nextThemeIsDark ? "\uE708" : "\uE793",
        };
    }

    /// <summary>Gets the effective app theme for the sample window.</summary>
    private ElementTheme GetEffectiveTheme()
    {
        return Content is FrameworkElement root && root.RequestedTheme != ElementTheme.Default
            ? root.RequestedTheme
            : Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? ElementTheme.Dark
                : ElementTheme.Light;
    }

    /// <summary>Applies theme-aware colors to the custom title bar and caption buttons.</summary>
    private void ApplyTitleBarTheme(ElementTheme theme)
    {
        if (_appWindow?.TitleBar is not { } titleBar)
            return;

        var transparent = Colors.Transparent;
        titleBar.BackgroundColor = transparent;
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.InactiveBackgroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;

        var isDark = theme == ElementTheme.Dark ||
            (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        var foreground = isDark ? Colors.White : Colors.Black;
        var hoverBg = isDark
            ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
        var pressedBg = isDark
            ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
        var inactiveFg = isDark
            ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x99, 0x00, 0x00, 0x00);

        titleBar.ForegroundColor = foreground;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hoverBg;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressedBg;
        titleBar.ButtonInactiveForegroundColor = inactiveFg;
    }

    #endregion

    #region API Key Lifecycle

    /// <summary>
    /// Post-layout initialization: prompts for the API key when the vault was empty
    /// (the ctor already handled the vault-hit path), then loads the model list. Runs
    /// after the visual tree is up so dialogs have a XamlRoot to attach to.
    /// </summary>
    private async void OnContentLoaded(object sender, RoutedEventArgs args)
    {
        ((FrameworkElement)Content).Loaded -= OnContentLoaded;

        if (_client is null)
        {
            // Vault-miss path. The modal prompt blocks send attempts naturally — the
            // user cannot interact with ChatPanel until the dialog returns.
            var apiKey = await PromptForApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Close();
                return;
            }
            SaveApiKeyToVault(apiKey);
            _client = new AnthropicClient { ApiKey = apiKey };
            await LoadModelsAsync(apiKey);
        }
        else
        {
            // Vault-hit path. Client was wired in the ctor; just populate the model list.
            await LoadModelsAsync(_client.ApiKey ?? string.Empty);
        }
    }

    /// <summary>Handles the Reset API Key button click.</summary>
    private async void OnResetKeyClicked(object sender, RoutedEventArgs args)
    {
        var dialog = new ContentDialog
        {
            Title = "Reset API Key",
            Content = "Remove the saved API key and enter a new one?",
            PrimaryButtonText = "Reset",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = GetEffectiveTheme(),
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        RemoveApiKeyFromVault();
        _client = null;

        // The new-key prompt is modal — the user can't send during that window.
        var newKey = await PromptForApiKeyAsync();
        if (string.IsNullOrWhiteSpace(newKey))
        {
            Close();
            return;
        }

        SaveApiKeyToVault(newKey);
        _client = new AnthropicClient { ApiKey = newKey };
        await LoadModelsAsync(newKey);
    }

    /// <summary>Shows a dialog to collect the API key from the user.</summary>
    private async Task<string?> PromptForApiKeyAsync()
    {
        var dialog = new ApiKeyPromptDialog
        {
            XamlRoot = Content.XamlRoot,
            RequestedTheme = GetEffectiveTheme(),
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog.ApiKey : null;
    }

    #endregion

    #region PasswordVault Helpers

    /// <summary>Loads the API key from Windows Credential Manager.</summary>
    private static string? LoadApiKeyFromVault()
    {
        try
        {
            var vault = new PasswordVault();
            var credential = vault.Retrieve(VaultResource, VaultUser);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Saves the API key to Windows Credential Manager (DPAPI-encrypted).</summary>
    private static void SaveApiKeyToVault(string apiKey)
    {
        var vault = new PasswordVault();
        try { vault.Remove(vault.Retrieve(VaultResource, VaultUser)); } catch { }
        vault.Add(new PasswordCredential(VaultResource, VaultUser, apiKey));
    }

    /// <summary>Removes the stored API key from Windows Credential Manager.</summary>
    private static void RemoveApiKeyFromVault()
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(VaultResource, VaultUser));
        }
        catch { }
    }

    #endregion

    #region Export

    /// <summary>
    /// Exports the active conversation as Markdown and copies it to the clipboard.
    /// Shows a brief confirmation via TeachingTip.
    /// </summary>
    /// <summary>
    /// Handles the New Chat menu click by clearing the conversation and resetting the panel.
    /// </summary>
    private void OnNewChatClick(object sender, RoutedEventArgs e)
    {
        if (IsConversationBusy()) return;

        ChatPanel.ClearConversation();
    }

    /// <summary>Refreshes conversation-scoped menu state when the app menu opens.</summary>
    private void OnMenuFlyoutOpening(object? sender, object e)
    {
        UpdateConversationMenuItems();
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var messages = ChatPanel.GetMessages();

        if (messages.Count == 0 || IsConversationBusy())
        {
            ExportTip.Title = "No messages to export";
            ExportTip.Subtitle = messages.Count == 0
                ? "Start a conversation first."
                : "Wait for the current response to finish.";
            ExportTip.IsOpen = true;
            return;
        }

        var result = ChatPanel.ExportToMarkdown();

        var package = new DataPackage();
        package.SetText(result.Markdown);
        Clipboard.SetContent(package);

        ExportTip.Title = "Copied to clipboard";
        ExportTip.Subtitle = result.Media.Count > 0
            ? $"{result.Media.Count} media file(s) not included in clipboard"
            : null;
        ExportTip.IsOpen = true;
    }

    #endregion

    /// <summary>Handles user message submission by streaming from the Anthropic API into the ChatPanel.</summary>
    /// <remarks>
    /// Runs the entire user turn — including any number of tool rounds — under a single
    /// <see cref="AssistantTurnHandle"/>. Each round streams from the Anthropic API into
    /// the same root assistant message; if the model emits tool_use blocks
    /// (<see cref="StreamResult.HasToolCalls"/>), the matching
    /// <see cref="IAssistTool.ExecuteAsync"/> runs for every call, and
    /// <see cref="ChatPanel.AppendToolRoundAsync"/> records the round so the chat surface
    /// draws an inline tool block under the active bubble. The loop terminates on the
    /// first round with no tool calls, on cancellation, or when <see cref="MaxToolRounds"/>
    /// is reached.
    /// </remarks>
    private async void OnUserMessageSubmitted(object? sender, MessageSentEventArgs e)
    {
        if (_client is null) return;

        var modelId = ChatPanel.SelectedModel?.ModelId ?? "claude-sonnet-4-6";

        // One handle covers the whole user turn. Multi-round tool calls accumulate text
        // and inline tool blocks into the same root assistant bubble, matching the
        // main app's UX (one assistant message per user turn, tool calls expand inline).
        await using var handle = ChatPanel.BeginAnthropicTurn("Claude", modelId);
        var ct = handle.CancellationToken;

        for (var round = 0; round < MaxToolRounds; round++)
        {
            // BuildAnthropicParams forwards the tool list as the SDK's Tool[] and enables
            // Anthropic prompt caching by default. MaxTokens caps each assistant response
            // (Anthropic per-model caps late 2025: opus-4-7 = 32k, sonnet-4-6 = 64k,
            // haiku-4-5 = 16k / 64k with the extended-output beta header).
            var parameters = ChatPanel.BuildAnthropicParams(modelId, maxTokens: 16384, _tools);

            var stream = _client.Messages.CreateStreaming(parameters, ct);

            StreamResult result;
            try
            {
                result = await handle.StreamAnthropicAsync(stream, ct);
                Helpers.LoggingService.LogInfo(
                    $"[SdkSample] Round {round} complete — tokens={result.Usage?.TotalTokens ?? 0}, " +
                    $"toolCalls={result.ToolCalls?.Count ?? 0}, " +
                    $"cache_write={result.Usage?.CacheCreationInputTokens ?? 0}, " +
                    $"cache_read={result.Usage?.CacheReadInputTokens ?? 0}");
            }
            catch (OperationCanceledException)
            {
                // User clicked Stop. Partial content is preserved by DisposeAsync.
                return;
            }
            catch (Exception ex)
            {
                handle.Message.Content += $"\n\n[Error: {ex.Message}]";
                return;
            }

            // No tool calls → assistant produced its final reply; loop ends.
            if (!result.HasToolCalls)
                return;

            // Execute every requested tool and record the round. AppendToolRoundAsync
            // appends the invisible tool-call assistant message (so the next BuildAnthropicParams
            // emits ToolUseBlockParam instead of 422-ing) plus the ChatRole.Tool result
            // messages (grouped into a single user MessageParam by the converter), and
            // renders an inline tool block in the chat UI.
            var interactions = new List<ToolInteraction>(result.ToolCalls!.Count);
            foreach (var call in result.ToolCalls!)
            {
                var (output, isError) = await ExecuteToolAsync(call, ct);
                interactions.Add(new ToolInteraction(call, output, isError));
            }
            await ChatPanel.AppendToolRoundAsync(handle, interactions);
        }

        Helpers.LoggingService.LogInfo($"[SdkSample] Tool loop hit MaxToolRounds={MaxToolRounds}.");
    }

    /// <summary>
    /// Routes a single <see cref="ToolCall"/> to the matching <see cref="IAssistTool"/> and
    /// returns the JSON payload to surface back to the model along with an error flag for
    /// the inline tool block UI. Unknown tools and execution failures are turned into
    /// <c>{"error": "..."}</c> responses so the model can recover.
    /// </summary>
    private async Task<(string Output, bool IsError)> ExecuteToolAsync(ToolCall call, CancellationToken ct)
    {
        var tool = _tools.FirstOrDefault(t =>
            string.Equals(t.Name, call.FunctionName, StringComparison.Ordinal));

        if (tool is null)
            return (JsonSerializer.Serialize(new { error = $"Unknown tool: {call.FunctionName}" }), IsError: true);

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.Arguments) ? "{}" : call.Arguments);
            var output = await tool.ExecuteAsync(doc.RootElement, ct);
            return (output, IsError: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (JsonSerializer.Serialize(new { error = $"{tool.Name} failed: {ex.Message}" }), IsError: true);
        }
    }

    /// <summary>Updates conversation-scoped menu items from message and streaming state.</summary>
    private void UpdateConversationMenuItems()
    {
        var messages = ChatPanel.GetMessages();
        var canUseConversationActions = messages.Count > 0 && !IsConversationBusy();
        NewChatMenuItem.IsEnabled = canUseConversationActions;
        ExportMenuItem.IsEnabled = canUseConversationActions;
    }

    /// <summary>Returns true while an assistant turn is still streaming.</summary>
    private bool IsConversationBusy() => ChatPanel.GetMessages().Any(m => m.IsStreaming);
}
