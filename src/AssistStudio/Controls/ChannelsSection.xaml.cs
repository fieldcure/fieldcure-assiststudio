using AssistStudio.Helpers;
using AssistStudio.Mcp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Text.Json;
using Windows.UI;

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section that displays configured Outbox messaging channels.
/// Reads channels via the <c>list_channels</c> MCP tool. Mutations are delegated
/// to the Outbox CLI (<c>fieldcure-mcp-outbox add|remove</c>) spawned in a new
/// console window; after the CLI exits we reconnect the Outbox server so the
/// list reflects the new channels.json state.
/// </summary>
public sealed partial class ChannelsSection : UserControl
{
    #region Fields

    private readonly ResourceLoader _loader = new();
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

    public ChannelsSection()
    {
        InitializeComponent();
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
    /// selected type, then reconnects the server so the UI picks up the new
    /// entry.
    /// </summary>
    private async void OnAddChannelTypeClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item || item.Tag is not string type)
            return;

        await RunCliAsync("add", type);
    }

    /// <summary>
    /// Launches the Outbox CLI in a new console window to remove a channel,
    /// then reconnects the server so the UI picks up the removal.
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

            BuildChannelList(channels);
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
    /// Builds the channel list UI from the MCP tool result, attaching a delete
    /// button per row that dispatches to the CLI <c>remove</c> command.
    /// </summary>
    private void BuildChannelList(JsonElement channels)
    {
        ChannelListPanel.Children.Clear();

        var deleteTooltip = _loader.GetString("Connect_DeleteChannel") ?? "Delete channel";

        foreach (var channel in channels.EnumerateArray())
        {
            var id = channel.TryGetProperty("id", out var i) ? i.GetString() ?? "" : "";
            var type = channel.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var name = channel.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var from = channel.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";

            var displayType = TypeDisplayNames.TryGetValue(type, out var dn) ? dn : type;
            var displayDetail = !string.IsNullOrEmpty(from) ? from : name;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Green status dot
            var dot = new FontIcon
            {
                Glyph = "\uF136",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(dot, 0);
            row.Children.Add(dot);

            // Channel type label (bold, fixed width)
            var typeLabel = new TextBlock
            {
                Text = displayType,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(typeLabel, 1);
            row.Children.Add(typeLabel);

            // Channel detail (name/from)
            var detail = new TextBlock
            {
                Text = displayDetail,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(detail, 2);
            row.Children.Add(detail);

            // Delete button — CLI remove
            var deleteButton = new Button
            {
                Tag = id,
                Padding = new Thickness(6, 2, 6, 2),
                Background = (Brush)Application.Current.Resources["SubtleFillColorTransparentBrush"],
                BorderThickness = new Thickness(0),
                Content = new FontIcon { Glyph = "\uE74D", FontSize = 12 },
            };
            ToolTipService.SetToolTip(deleteButton, deleteTooltip);
            ToolTipService.SetPlacement(deleteButton, Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse);
            deleteButton.Click += OnDeleteChannelClick;
            Grid.SetColumn(deleteButton, 3);
            row.Children.Add(deleteButton);

            ChannelListPanel.Children.Add(row);
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelListPanel.Visibility = Visibility.Visible;
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
        ChannelListPanel.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows a status/error message and hides other states.
    /// </summary>
    private void ShowStatus(string message)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelListPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        StatusText.Visibility = Visibility.Visible;
    }

    #endregion
}
