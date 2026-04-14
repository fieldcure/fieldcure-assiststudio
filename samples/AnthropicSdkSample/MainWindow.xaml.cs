using Anthropic;
using Anthropic.Models.Messages;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Controls.Anthropic;
using FieldCure.AssistStudio.Controls.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Security.Credentials;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace AnthropicSdkSample;

/// <summary>
/// Demonstrates ChatPanel integration with the Anthropic C# SDK.
/// On first launch, prompts for an API key and stores it securely in Windows Credential Manager.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>Available Anthropic models for the model selector.</summary>
    private static readonly (string Id, string Display)[] AvailableModels =
    [
        ("claude-opus-4-6",   "Claude Opus 4.6"),
        ("claude-sonnet-4-6", "Claude Sonnet 4.6"),
        ("claude-haiku-4-5",  "Claude Haiku 4.5"),
    ];

    /// <summary>LocalSettings key for the persisted model selection.</summary>
    private const string ModelSettingName = "SelectedModelId";

    /// <summary>PasswordVault resource name for credential storage.</summary>
    private const string VaultResource = "AnthropicSdkSample";

    /// <summary>PasswordVault user name (single-key app, so a fixed name suffices).</summary>
    private const string VaultUser = "ApiKey";

    /// <summary>Anthropic SDK client. Null until API key is verified on first load.</summary>
    private AnthropicClient? _client;

    /// <summary>Currently selected model ID.</summary>
    private string _selectedModelId = AvailableModels[1].Id; // Default: Sonnet

    /// <summary>Guards against saving during programmatic ComboBox population.</summary>
    private bool _suppressModelChanged;

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

        // Populate model selector
        InitializeModelSelector();

        ChatPanel.UserMessageSubmitted += OnUserMessageSubmitted;
        ((FrameworkElement)Content).Loaded += OnContentLoaded;
    }

    #region Model Selector

    /// <summary>Populates the model ComboBox and restores the last selection from settings.</summary>
    private void InitializeModelSelector()
    {
        _suppressModelChanged = true;

        // Restore saved selection
        var settings = ApplicationData.Current.LocalSettings;
        var savedModelId = settings.Values[ModelSettingName] as string;

        var selectedIndex = 1; // Default: Sonnet
        for (var i = 0; i < AvailableModels.Length; i++)
        {
            ModelComboBox.Items.Add(AvailableModels[i].Display);
            if (AvailableModels[i].Id == savedModelId)
                selectedIndex = i;
        }

        ModelComboBox.SelectedIndex = selectedIndex;
        _selectedModelId = AvailableModels[selectedIndex].Id;

        _suppressModelChanged = false;
    }

    /// <summary>Handles model ComboBox selection changes.</summary>
    private void OnModelSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressModelChanged) return;
        if (ModelComboBox.SelectedIndex < 0 || ModelComboBox.SelectedIndex >= AvailableModels.Length) return;

        _selectedModelId = AvailableModels[ModelComboBox.SelectedIndex].Id;

        var settings = ApplicationData.Current.LocalSettings;
        settings.Values[ModelSettingName] = _selectedModelId;
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
    }

    /// <summary>Shows a dialog to collect the API key from the user.</summary>
    private async Task<string?> PromptForApiKeyAsync()
    {
        var panel = new StackPanel { Spacing = 8 };
        var input = new TextBox
        {
            PlaceholderText = "sk-ant-api03-...",
            AcceptsReturn = false,
        };
        panel.Children.Add(input);
        panel.Children.Add(new Microsoft.UI.Xaml.Controls.TextBlock
        {
            Text = "Your key is stored securely in Windows Credential Manager (DPAPI-encrypted) and never leaves this device.",
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });

        var dialog = new ContentDialog
        {
            Title = "Enter Anthropic API Key",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Exit",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text : null;
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
    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var messages = ChatPanel.GetMessages();

        if (messages.Count == 0)
        {
            ExportTip.Title = "No messages to export";
            ExportTip.Subtitle = "Start a conversation first.";
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

        ExportButton.IsEnabled = true;

        var modelId = _selectedModelId;

        // Begin an assistant turn — this creates the message bubble and shows the streaming cursor
        await using var handle = ChatPanel.BeginAnthropicTurn("Claude", modelId);

        // Convert the current conversation to Anthropic SDK format
        var conv = ChatPanel.GetConversationAsAnthropicMessages();

        // Stream directly from the Anthropic SDK, wired to the Stop button
        var ct = handle.CancellationToken;
        var stream = _client.Messages.CreateStreaming(new MessageCreateParams
        {
            Model = modelId,
            System = conv.SystemPrompt is not null
                ? new MessageCreateParamsSystem(conv.SystemPrompt)
                : null,
            Messages = conv.Messages,
            MaxTokens = 4096,
        }, ct);

        try
        {
            await handle.StreamAnthropicAsync(stream, ct);
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
}
