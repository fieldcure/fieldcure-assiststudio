using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.Resources;
using Windows.UI;

namespace AssistStudio.Controls;

/// <summary>
/// Card control that displays an MCP server connection with status, details, and actions.
/// </summary>
public sealed partial class McpServerCard : UserControl
{
    #region Dependency Properties

    /// <summary>Identifies the <see cref="Connection"/> dependency property.</summary>
    public static readonly DependencyProperty ConnectionProperty =
        DependencyProperty.Register(
            nameof(Connection),
            typeof(McpServerConnection),
            typeof(McpServerCard),
            new PropertyMetadata(null, OnConnectionChanged));

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private bool _suppressToggleEvent;

    #endregion

    #region Events

    /// <summary>Raised when the user toggles the enable switch.</summary>
    public event EventHandler<McpServerConnection>? EnableToggled;

    /// <summary>Raised when the user clicks the reconnect button.</summary>
    public event EventHandler<McpServerConnection>? ReconnectRequested;

    /// <summary>Raised when the user clicks the delete button.</summary>
    public event EventHandler<McpServerConnection>? DeleteRequested;

    #endregion

    #region Constructor

    public McpServerCard()
    {
        InitializeComponent();
    }

    #endregion

    #region Properties

    /// <summary>Gets the current state of the enable toggle.</summary>
    public bool IsToggleOn => EnableToggle.IsOn;

    /// <summary>
    /// Gets or sets the MCP server connection to display.
    /// </summary>
    public McpServerConnection? Connection
    {
        get => (McpServerConnection?)GetValue(ConnectionProperty);
        set => SetValue(ConnectionProperty, value);
    }

    #endregion

    #region Private Methods

    private static void OnConnectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is McpServerCard card)
            card.UpdateUI();
    }

    private void UpdateUI()
    {
        var connection = Connection;
        if (connection is null) return;

        var config = connection.Config;

        // Status color
        var statusColor = connection.State switch
        {
            McpConnectionState.Connected => Color.FromArgb(255, 76, 175, 80),   // Green
            McpConnectionState.Connecting => Color.FromArgb(255, 255, 193, 7),  // Yellow
            McpConnectionState.Error => Color.FromArgb(255, 244, 67, 54),       // Red
            _ => Color.FromArgb(255, 158, 158, 158),                            // Gray
        };
        StatusIcon.Foreground = new SolidColorBrush(statusColor);

        // Server name + transport
        ServerNameText.Text = config.Name;
        TransportText.Text = config.TransportType.ToString();

        // Detail text
        var detailText = config.TransportType == McpTransportType.Stdio
            ? $"{config.Command} {string.Join(" ", config.Arguments ?? [])}"
            : config.Url ?? "";

        var toolsLabel = _loader.GetString("Connect_Tools");
        var toolCount = connection.IsConnected ? $" · {connection.Tools.Count} {toolsLabel}" : "";
        var errorText = connection.State == McpConnectionState.Error ? $" · {connection.ErrorMessage}" : "";
        DetailText.Text = detailText + toolCount + errorText;

        // Toggle state (suppress event to avoid re-entry)
        _suppressToggleEvent = true;
        EnableToggle.IsOn = config.IsEnabled;
        _suppressToggleEvent = false;

        // Tooltips
        ToolTipService.SetToolTip(ReconnectButton, _loader.GetString("Connect_Reconnect"));
        ToolTipService.SetToolTip(DeleteButton, _loader.GetString("Connect_Remove"));
    }

    #endregion

    #region Event Handlers

    private void EnableToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        if (Connection is not null)
            EnableToggled?.Invoke(this, Connection);
    }

    private void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (Connection is not null)
            ReconnectRequested?.Invoke(this, Connection);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Connection is not null)
            DeleteRequested?.Invoke(this, Connection);
    }

    #endregion
}
