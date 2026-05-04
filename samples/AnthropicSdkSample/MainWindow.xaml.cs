using Anthropic;
using AnthropicSdkSample.Controls;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Controls.Anthropic;
using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    /// <summary>Anthropic SDK client. Null until API key is verified on first load.</summary>
    private AnthropicClient? _client;

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

    /// <summary>Loads or prompts for the API key once the visual tree is ready.</summary>
    private async void OnContentLoaded(object sender, RoutedEventArgs args)
    {
        ((FrameworkElement)Content).Loaded -= OnContentLoaded;

        var apiKey = LoadApiKeyFromVault();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = await PromptForApiKeyAsync();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Close();
                return;
            }
            SaveApiKeyToVault(apiKey);
        }

        _client = new AnthropicClient { ApiKey = apiKey };
        await LoadModelsAsync(apiKey);
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
    private async void OnUserMessageSubmitted(object? sender, MessageSentEventArgs e)
    {
        if (_client is null) return;

        var modelId = ChatPanel.SelectedModel?.ModelId ?? "claude-sonnet-4-6";

        // Begin an assistant turn — this creates the message bubble and shows the streaming cursor
        await using var handle = ChatPanel.BeginAnthropicTurn("Claude", modelId);

        // Build SDK params via the Controls.Anthropic helper. The helper enables Anthropic
        // prompt caching by default, so consumers benefit from cache hits on repeated prefixes
        // (system prompt, attachments, tool results) simply by using this entry point.
        //
        // MaxTokens caps the assistant response length. 16k is comfortable for typical
        // artifacts (Lorenz/Mandelbrot visualizers, dashboard JSX, ~200-line React
        // components) without tripping the truncated-hint banner. Per-model output caps
        // (Anthropic, late 2025): claude-opus-4-7 = 32k, claude-sonnet-4-6 = 64k,
        // claude-haiku-4-5 = 16k (64k with the extended-output beta header). Bump this
        // when you routinely see truncated responses, or wire it to a per-model lookup
        // if you ship multiple model choices to end users.
        var parameters = ChatPanel.BuildAnthropicParams(modelId, maxTokens: 16384);

        // Stream directly from the Anthropic SDK, wired to the Stop button
        var ct = handle.CancellationToken;
        var stream = _client.Messages.CreateStreaming(parameters, ct);

        try
        {
            var result = await handle.StreamAnthropicAsync(stream, ct);
            Helpers.LoggingService.LogInfo(
                $"[SdkSample] Response complete — tokens={result.Usage?.TotalTokens ?? 0}, " +
                $"cache_write={result.Usage?.CacheCreationInputTokens ?? 0}, " +
                $"cache_read={result.Usage?.CacheReadInputTokens ?? 0}");
        }
        catch (OperationCanceledException)
        {
            // User clicked Stop — partial content is preserved by DisposeAsync
        }
        catch (Exception ex)
        {
            // Best-effort error display. Renderer may not pick up post-finalize updates;
            // a ContentDialog would be more reliable for production code.
            handle.Message.Content += $"\n\n[Error: {ex.Message}]";
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
