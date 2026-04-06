using System.Text.Json;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.Resources;
using Windows.UI;

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section that displays configured Outbox messaging channels.
/// Calls the <c>list_channels</c> MCP tool to fetch the channel list.
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
                    ?? "No channels configured. Add channels in a conversation.");
                return;
            }

            using var doc = JsonDocument.Parse(resultJson);
            if (!doc.RootElement.TryGetProperty("channels", out var channels)
                || channels.GetArrayLength() == 0)
            {
                ShowStatus(_loader.GetString("Connect_NoChannels")
                    ?? "No channels configured. Add channels in a conversation.");
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
    /// Builds the channel list UI from the MCP tool result.
    /// </summary>
    private void BuildChannelList(JsonElement channels)
    {
        ChannelListPanel.Children.Clear();

        foreach (var channel in channels.EnumerateArray())
        {
            var type = channel.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var name = channel.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var from = channel.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";

            var displayType = TypeDisplayNames.TryGetValue(type, out var dn) ? dn : type;
            var displayDetail = !string.IsNullOrEmpty(from) ? from : name;

            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };

            // Green status dot
            row.Children.Add(new FontIcon
            {
                Glyph = "\uF136",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 76, 175, 80)),
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Channel type label (bold, fixed width)
            row.Children.Add(new TextBlock
            {
                Text = displayType,
                Width = 80,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });

            // Channel detail (name/from)
            row.Children.Add(new TextBlock
            {
                Text = displayDetail,
                Opacity = 0.7,
                VerticalAlignment = VerticalAlignment.Center,
            });

            ChannelListPanel.Children.Add(row);
        }

        LoadingPanel.Visibility = Visibility.Collapsed;
        ChannelListPanel.Visibility = Visibility.Visible;
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
