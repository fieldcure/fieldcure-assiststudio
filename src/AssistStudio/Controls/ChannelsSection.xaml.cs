using AssistStudio.Helpers;
using AssistStudio.Mcp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section that displays configured Outbox messaging channels.
/// Reads channels via the <c>list_channels</c> MCP tool. Mutations are delegated
/// to the Outbox CLI (<c>fieldcure-mcp-outbox add|remove</c>) spawned in a new
/// console window; after the CLI exits we reload the list so the UI reflects
/// the new channels.json state.
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
        AddChannelText.Text = _loader.GetString("Connect_AddChannel") ?? "Add channel";
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
    /// Launches the Outbox CLI in a new console window to add a channel of the
    /// selected type, then reloads the list so the UI picks up the new entry.
    /// </summary>
    private async void OnAddChannelTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string type)
            return;

        await RunCliAsync("add", type);
    }

    /// <summary>
    /// Launches the Outbox CLI in a new console window to remove a channel,
    /// then reloads the list so the UI picks up the removal.
    /// </summary>
    private async void OnDeleteChannelClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string channelId)
            return;

        await RunCliAsync("remove", channelId);
    }

    #endregion

    #region Private Methods

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
                ShowStatus(_loader.GetString("Connect_NoChannels")
                    ?? "No channels configured yet.");
                return;
            }

            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("channels", out var channels)
                || channels.GetArrayLength() == 0)
            {
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
            var displayDetail = !string.IsNullOrEmpty(from) ? from : name;

            _channels.Add(new ChannelRowViewModel(id, displayType, displayDetail, deleteTooltip));
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelList.Visibility = Visibility.Visible;
        StatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Spawns the Outbox CLI (<c>dnx FieldCure.Mcp.Outbox@... &lt;command&gt; &lt;arg&gt;</c>)
    /// in its own console window and waits for it to exit. No server reconnect
    /// is needed afterwards: <c>ChannelStore.LoadAsync</c> reads
    /// <c>channels.json</c> on every tool call, so the next <c>list_channels</c>
    /// already reflects the CLI's changes.
    /// </summary>
    private async Task RunCliAsync(string command, string arg)
    {
        AddChannelButton.IsEnabled = false;

        try
        {
            var (_, prefixArgs) = BuiltInServerHelper.GetLaunchSpec(BuiltInServerHelper.OutboxKey);
            if (prefixArgs.Length == 0)
            {
                LoggingService.LogError("[Outbox] No launch spec available for Outbox CLI");
                return;
            }

            // UseShellExecute=true honors PATHEXT so "dnx" resolves to dnx.cmd,
            // and the .cmd shim opens its own console window for interactive prompts.
            var args = string.Join(' ', prefixArgs) + $" {command} {arg}";
            var psi = new ProcessStartInfo
            {
                FileName = "dnx",
                Arguments = args,
                UseShellExecute = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                LoggingService.LogError($"[Outbox] Failed to launch CLI for '{command} {arg}'");
                return;
            }

            await process.WaitForExitAsync();
            LoggingService.LogInfo($"[Outbox] CLI '{command} {arg}' exited with code {process.ExitCode}");

            await ReloadAsync();
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[Outbox] CLI spawn failed: {ex.Message}");
        }
        finally
        {
            AddChannelButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Reloads the channel list by re-invoking <c>list_channels</c>. The Outbox
    /// server re-reads <c>channels.json</c> on every call, so no reconnect is
    /// required to surface CLI-driven changes.
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

    #endregion
}

/// <summary>
/// View model for one row inside <see cref="ChannelsSection"/>'s channel list.
/// Immutable projection of one Outbox <c>list_channels</c> entry.
/// </summary>
public sealed class ChannelRowViewModel
{
    /// <summary>Initializes the row with display-ready fields resolved from the MCP payload.</summary>
    /// <param name="id">Channel identifier used by the CLI <c>remove</c> command.</param>
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

    /// <summary>Gets the channel identifier passed back to the CLI <c>remove</c> command.</summary>
    public string Id { get; }

    /// <summary>Gets the localized channel type label.</summary>
    public string DisplayType { get; }

    /// <summary>Gets the channel detail (from address or display name).</summary>
    public string DisplayDetail { get; }

    /// <summary>Gets the tooltip shown on the row's delete button.</summary>
    public string DeleteTooltip { get; }
}
