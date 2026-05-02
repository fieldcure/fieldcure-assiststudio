using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section that displays configured Outbox messaging channels.
/// Reads and mutates channels through the Outbox MCP tools, then reloads the
/// list so the UI reflects the new channel store state.
/// </summary>
public sealed partial class ChannelsSection : UserControl
{
    #region Fields

    private readonly ResourceLoader _loader = new();
    private readonly ObservableCollection<ChannelRowViewModel> _channels = [];
    private McpServerRegistry? _registry;
    private bool _loaded;

    /// <summary>
    /// Maps channel type strings from the Outbox MCP to display names.
    /// </summary>
    private static readonly Dictionary<string, string> TypeDisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["smtp"] = "SMTP",
        ["microsoft"] = "MS Graph",
        ["slack"] = "Slack",
        ["discord"] = "Discord",
        ["telegram"] = "Telegram",
        ["kakaotalk"] = "KakaoTalk",
    };

    #endregion

    #region Constructor

    /// <summary>Initializes the control and binds the channel list to its backing collection.</summary>
    public ChannelsSection()
    {
        InitializeComponent();
        ChannelList.ItemsSource = _channels;
        ApplyLocalizedText();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes the section with the MCP server registry and applies localized header text.
    /// </summary>
    public void Initialize(McpServerRegistry registry)
    {
        _registry = registry;
        Section.Header = _loader.GetString("Connect_Channels");
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Lazy-loads the channel list on first expand by calling the Outbox MCP server.
    /// </summary>
    private void OnSectionExpanded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;

        LoadingText.Text = _loader.GetString("Connect_Channels") ?? "Channels";
        _ = LoadChannelsAsync();
    }

    /// <summary>
    /// Adds a channel of the selected type through the Outbox MCP server.
    /// </summary>
    private async void OnAddChannelTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string type)
            return;

        await AddChannelAsync(type);
    }

    /// <summary>
    /// Confirms and removes a channel through the Outbox MCP server.
    /// </summary>
    private async void OnDeleteChannelClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string channelId)
            return;

        if (!await ConfirmDeleteAsync(channelId))
            return;

        await RemoveChannelAsync(channelId);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Applies localized text for elements referenced from code.
    /// </summary>
    private void ApplyLocalizedText()
    {
        AddChannelText.Text = _loader.GetString("Connect_AddChannel") ?? "Add channel";
        AddSlackItem.Text = GetChannelTypeLabel("slack");
        AddDiscordItem.Text = GetChannelTypeLabel("discord");
        AddGmailItem.Text = GetChannelTypeLabel("gmail");
        AddNaverItem.Text = GetChannelTypeLabel("naver");
        AddSmtpItem.Text = GetChannelTypeLabel("smtp");
        AddTelegramItem.Text = GetChannelTypeLabel("telegram");
        AddKakaoItem.Text = GetChannelTypeLabel("kakaotalk");
        AddMicrosoftItem.Text = GetChannelTypeLabel("microsoft");
    }

    /// <summary>
    /// Fetches the channel list from the Outbox MCP server with connection polling.
    /// </summary>
    private async Task LoadChannelsAsync()
    {
        ShowLoading();

        var conn = await WaitForOutboxAsync();
        if (conn is null)
        {
            ShowStatus(_loader.GetString("Connect_OutboxNotConnected") ?? "Unable to load channels");
            return;
        }

        try
        {
            var emptyArgs = JsonDocument.Parse("{}").RootElement;
            var resultJson = await conn.CallToolWithProgressAsync(
                "list_channels", emptyArgs, null, CancellationToken.None);

            if (string.IsNullOrEmpty(resultJson))
            {
                _channels.Clear();
                ShowStatus(_loader.GetString("Connect_NoChannels")
                    ?? "No channels configured yet.");
                return;
            }

            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("channels", out var channels)
                || channels.GetArrayLength() == 0)
            {
                _channels.Clear();
                ShowStatus(_loader.GetString("Connect_NoChannels")
                    ?? "No channels configured yet.");
                return;
            }

            PopulateChannelList(channels);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] list_channels failed: {ex.Message}");
            ShowStatus(_loader.GetString("Connect_OutboxNotConnected") ?? "Unable to load channels");
        }
    }

    /// <summary>
    /// Polls for the Outbox MCP connection (max 10 seconds).
    /// </summary>
    private async Task<McpServerConnection?> WaitForOutboxAsync()
    {
        for (int i = 0; i < 20; i++)
        {
            var conn = _registry?.GetBuiltInConnection(BuiltInServerHelper.OutboxKey);
            if (conn?.IsConnected == true)
                return conn;
            await Task.Delay(500);
        }

        return null;
    }

    /// <summary>
    /// Projects the Outbox <c>list_channels</c> result into <see cref="ChannelRowViewModel"/>
    /// instances and swaps the panel over to the channel list.
    /// </summary>
    private void PopulateChannelList(JsonElement channels)
    {
        var deleteTooltip = _loader.GetString("Connect_DeleteChannel") ?? "Delete channel";

        _channels.Clear();
        foreach (var channel in channels.EnumerateArray())
        {
            var id = channel.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            var type = channel.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var name = channel.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var from = channel.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";

            var displayType = TypeDisplayNames.TryGetValue(type, out var dn) ? dn : type;
            displayType = ResolveDisplayType(id, type, name, displayType);
            var displayDetail = !string.IsNullOrEmpty(from) ? from : name;

            _channels.Add(new ChannelRowViewModel(id, displayType, displayDetail, deleteTooltip));
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelList.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Resolves display labels for provider-specific SMTP shortcuts.
    /// </summary>
    private static string ResolveDisplayType(
        string id,
        string type,
        string name,
        string fallback)
    {
        if (!string.Equals(type, "smtp", StringComparison.OrdinalIgnoreCase))
            return fallback;

        if (id.StartsWith("smtp_gmail_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Gmail", StringComparison.OrdinalIgnoreCase))
        {
            return "Gmail";
        }

        if (id.StartsWith("smtp_naver_", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Naver", StringComparison.OrdinalIgnoreCase))
        {
            return "Naver";
        }

        return fallback;
    }

    /// <summary>
    /// Adds an Outbox channel by invoking the MCP <c>add_channel</c> tool.
    /// </summary>
    private async Task AddChannelAsync(string type)
    {
        AddChannelButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;
        ElicitationPresenterScope? presenterScope = null;

        try
        {
            var conn = await WaitForOutboxAsync();
            if (conn is null)
            {
                PostChannelAddFailed(
                    GetChannelTypeLabel(type),
                    _loader.GetString("Connect_OutboxNotConnected") ?? "Unable to load channels");
                return;
            }

            using var argsDoc = JsonDocument.Parse(JsonSerializer.Serialize(new { type }));
            if (XamlRoot is null)
            {
                PostChannelAddFailed(
                    GetChannelTypeLabel(type),
                    _loader.GetString("Connect_OutboxNotConnected") ?? "Unable to load channels");
                return;
            }

            presenterScope = conn.PushElicitationPresenter(
                new DialogElicitationPresenter(XamlRoot, DispatcherQueue));

            var resultJson = await conn.CallToolWithProgressAsync(
                "add_channel", argsDoc.RootElement, null, CancellationToken.None);

            if (!IsOkResult(resultJson, out var error))
            {
                LoggingService.LogError($"[Outbox] add_channel failed for '{type}': {error}");
                if (presenterScope.WasCancelled)
                {
                    PostChannelAddCancelled(GetChannelTypeLabel(type));
                    return;
                }

                PostChannelAddFailed(
                    GetChannelTypeLabel(type),
                    error ?? (_loader.GetString("Connect_OutboxNotConnected")
                        ?? "Unable to load channels"));
                return;
            }

            LoggingService.LogInfo($"[Outbox] Channel added via MCP: {type}");
            await ReloadAsync();
            PostChannelAddSucceeded(GetChannelTypeLabel(type));
        }
        catch (OperationCanceledException)
        {
            PostChannelAddCancelled(GetChannelTypeLabel(type));
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[Outbox] add_channel failed: {ex.Message}");
            if (presenterScope?.WasCancelled == true)
            {
                PostChannelAddCancelled(GetChannelTypeLabel(type));
                return;
            }

            PostChannelAddFailed(GetChannelTypeLabel(type), ex.Message);
        }
        finally
        {
            presenterScope?.Dispose();
            AddChannelButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Posts a success notification after a channel is added.
    /// </summary>
    private void PostChannelAddSucceeded(string channelType)
    {
        NotificationCenter.Instance.Post(
            InfoBarSeverity.Success,
            _loader.GetString("Connect_ChannelAddSucceededTitle") ?? "Channel added",
            string.Format(
                _loader.GetString("Connect_ChannelAddSucceededMessage")
                    ?? "{0} channel is ready.",
                channelType));
    }

    /// <summary>
    /// Posts a cancellation notification after channel setup exits.
    /// </summary>
    private void PostChannelAddCancelled(string channelType)
    {
        NotificationCenter.Instance.Post(
            InfoBarSeverity.Informational,
            _loader.GetString("Connect_ChannelAddCancelledTitle") ?? "Channel setup cancelled",
            channelType);
    }

    /// <summary>
    /// Posts a failure notification after channel setup exits.
    /// </summary>
    private void PostChannelAddFailed(string channelType, string message)
    {
        NotificationCenter.Instance.Post(
            InfoBarSeverity.Error,
            _loader.GetString("Connect_ChannelAddFailedTitle") ?? "Channel setup failed",
            message);
    }

    /// <summary>
    /// Returns a localized display label for an add-channel menu type.
    /// </summary>
    private string GetChannelTypeLabel(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "slack" => _loader.GetString("Connect_ChannelTypeSlack") ?? "Slack",
            "discord" => _loader.GetString("Connect_ChannelTypeDiscord") ?? "Discord",
            "gmail" => _loader.GetString("Connect_ChannelTypeGmail") ?? "Gmail",
            "naver" => _loader.GetString("Connect_ChannelTypeNaver") ?? "Naver",
            "smtp" => _loader.GetString("Connect_ChannelTypeSmtpCustom") ?? "SMTP (custom)",
            "telegram" => _loader.GetString("Connect_ChannelTypeTelegram") ?? "Telegram",
            "kakaotalk" => _loader.GetString("Connect_ChannelTypeKakaoTalk") ?? "KakaoTalk",
            "microsoft" => _loader.GetString("Connect_ChannelTypeMicrosoft") ?? "Microsoft (Outlook)",
            _ => type,
        };
    }

    /// <summary>
    /// Shows a confirmation dialog before removing an Outbox channel.
    /// </summary>
    private async Task<bool> ConfirmDeleteAsync(string channelId)
    {
        var dialog = new ChannelDeleteConfirmDialog(channelId) { XamlRoot = XamlRoot };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>
    /// Removes an Outbox channel by invoking the MCP <c>remove_channel</c> tool.
    /// </summary>
    private async Task RemoveChannelAsync(string channelId)
    {
        AddChannelButton.IsEnabled = false;
        StatusText.Visibility = Visibility.Collapsed;

        try
        {
            var conn = await WaitForOutboxAsync();
            if (conn is null)
            {
                ShowActionStatus(_loader.GetString("Connect_OutboxNotConnected")
                    ?? "Unable to load channels");
                return;
            }

            using var argsDoc = JsonDocument.Parse(JsonSerializer.Serialize(new { channel = channelId }));
            var resultJson = await conn.CallToolWithProgressAsync(
                "remove_channel", argsDoc.RootElement, null, CancellationToken.None);

            if (!IsOkResult(resultJson, out var error))
            {
                LoggingService.LogError($"[Outbox] remove_channel failed for '{channelId}': {error}");
                ShowActionStatus(error ?? (_loader.GetString("Connect_OutboxNotConnected")
                    ?? "Unable to load channels"));
                return;
            }

            LoggingService.LogInfo($"[Outbox] Channel removed via MCP: {channelId}");
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[Outbox] remove_channel failed: {ex.Message}");
            ShowActionStatus(_loader.GetString("Connect_OutboxNotConnected")
                ?? "Unable to load channels");
        }
        finally
        {
            AddChannelButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Parses a simple Outbox tool result and extracts an optional error message.
    /// </summary>
    private static bool IsOkResult(string? resultJson, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(resultJson))
            return false;

        using var doc = JsonDocument.Parse(resultJson);
        if (!doc.RootElement.TryGetProperty("status", out var status))
            return false;

        if (string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            return true;

        error = doc.RootElement.TryGetProperty("error", out var errorElement)
            ? errorElement.GetString()
            : null;
        return false;
    }

    /// <summary>
    /// Reloads the channel list by re-invoking <c>list_channels</c>. The Outbox
    /// server re-reads its channel store on every call, so no reconnect is
    /// required to surface changes.
    /// </summary>
    private async Task ReloadAsync()
    {
        _loaded = true;
        await LoadChannelsAsync();
    }

    /// <summary>
    /// Shows the loading indicator and hides other states.
    /// </summary>
    private void ShowLoading()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        ChannelList.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows a status/error message and hides other states.
    /// </summary>
    private void ShowStatus(string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelList.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Shows a non-blocking add/remove status while preserving the current list.
    /// </summary>
    private void ShowActionStatus(string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelList.Visibility = _channels.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    #endregion
}

/// <summary>
/// View model for one row inside <see cref="ChannelsSection"/>'s channel list.
/// Immutable projection of one Outbox <c>list_channels</c> entry.
/// </summary>
public sealed class ChannelRowViewModel
{
    /// <summary>Initializes the row with display-ready fields resolved from the MCP payload.</summary>
    /// <param name="id">Channel identifier used by the MCP <c>remove_channel</c> command.</param>
    /// <param name="displayType">Localized channel type label (bold column).</param>
    /// <param name="displayDetail">Channel name or <c>from</c> address (ellipsized).</param>
    /// <param name="deleteTooltip">Tooltip shown on the row's delete button.</param>
    public ChannelRowViewModel(string id, string displayType, string displayDetail, string deleteTooltip)
    {
        Id = id;
        DisplayType = displayType;
        DisplayDetail = displayDetail;
        DeleteTooltip = deleteTooltip;
    }

    /// <summary>Gets the channel identifier passed back to the MCP <c>remove_channel</c> command.</summary>
    public string Id { get; }

    /// <summary>Gets the localized channel type label.</summary>
    public string DisplayType { get; }

    /// <summary>Gets the channel detail (from address or display name).</summary>
    public string DisplayDetail { get; }

    /// <summary>Gets the tooltip shown on the row's delete button.</summary>
    public string DeleteTooltip { get; }
}
