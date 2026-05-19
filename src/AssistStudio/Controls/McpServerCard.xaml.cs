using AssistStudio.Mcp;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
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

    /// <summary>Identifies the <see cref="BottomContent"/> dependency property.</summary>
    public static readonly DependencyProperty BottomContentProperty =
        DependencyProperty.Register(
            nameof(BottomContent),
            typeof(UIElement),
            typeof(McpServerCard),
            new PropertyMetadata(null, OnBottomContentChanged));

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private bool _suppressToggleEvent;

    #endregion

    #region Events

    /// <summary>Raised when the user toggles the enable switch.</summary>
    public event EventHandler<McpServerConnection>? EnableToggled;

    /// <summary>Raised when the user clicks the edit button.</summary>
    public event EventHandler<McpServerConnection>? EditRequested;

    /// <summary>Raised when the user clicks the reconnect button.</summary>
    public event EventHandler<McpServerConnection>? ReconnectRequested;

    /// <summary>Raised when the user clicks the delete button.</summary>
    public event EventHandler<McpServerConnection>? DeleteRequested;

    #endregion

    #region Constructor

    /// <summary>Initializes a new <see cref="McpServerCard"/>.</summary>
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

    /// <summary>
    /// Gets or sets optional content displayed below the card info (e.g., CollapsibleSection).
    /// </summary>
    public UIElement? BottomContent
    {
        get => (UIElement?)GetValue(BottomContentProperty);
        set => SetValue(BottomContentProperty, value);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Rebinds to the active connection and refreshes the card when the displayed
    /// MCP server connection changes.
    /// </summary>
    private static void OnConnectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not McpServerCard card) return;

        // Unsubscribe from old connection
        if (e.OldValue is McpServerConnection oldConn)
            oldConn.PropertyChanged -= card.OnConnectionPropertyChanged;

        // Subscribe to new connection
        if (e.NewValue is McpServerConnection newConn)
            newConn.PropertyChanged += card.OnConnectionPropertyChanged;

        card.UpdateUI();
    }

    /// <summary>
    /// Queues a UI refresh when a property on the bound connection changes.
    /// </summary>
    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(UpdateUI);
    }

    /// <summary>
    /// Updates the bottom content presenter visibility when the BottomContent dependency property changes.
    /// </summary>
    private static void OnBottomContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is McpServerCard card)
            card.BottomContentPresenter.Visibility = e.NewValue is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>
    /// Re-renders the card's status, detail text, version, actions, and tooltips
    /// from the current connection snapshot.
    /// </summary>
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
        var toolsLabel = _loader.GetString("Connect_Tools");
        var builtInLabel = _loader.GetString("Connect_BuiltIn");

        // Built-in badge in title row
        BuiltInBadge.Visibility = config.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed;
        BuiltInText.Text = builtInLabel;

        string detailText;
        if (config.IsBuiltIn)
        {
            if (connection.IsConnected && connection.Tools.Count > 0)
            {
                detailText = $"{connection.Tools.Count} {toolsLabel}";
            }
            else if (connection.State == McpConnectionState.Error
                     && !string.IsNullOrEmpty(connection.ErrorMessage))
            {
                // Surface the error on built-in cards too — without this, a red status dot
                // is silent (the dot color was set above but the detail row would fall
                // through to the descriptive placeholder text, hiding the actual cause).
                detailText = connection.ErrorMessage;
            }
            else
            {
                // Covers two cases: (a) genuinely disconnected (gray dot, default
                // placeholder description), and (b) Filesystem placeholder forced
                // Connected via SetPlaceholderConnected — the card has no Tools of its
                // own because each per-tab instance owns them, so we show the
                // active-instance description ("N active instance(s)") instead of
                // a misleading "0 tools".
                detailText = config.Description ?? "";
            }
        }
        else
        {
            var cmdText = config.TransportType == McpTransportType.Stdio
                ? $"{config.Command} {string.Join(" ", config.Arguments ?? [])}"
                : config.Url ?? "";
            var toolCount = connection.IsConnected ? $" · {connection.Tools.Count} {toolsLabel}" : "";
            var errorText = connection.State == McpConnectionState.Error ? $" · {connection.ErrorMessage}" : "";
            detailText = cmdText + toolCount + errorText;
        }
        DetailText.Text = detailText;

        // Version: prefer MCP handshake, fall back to NuGet package version for built-in servers
        var version = connection.ServerVersion;
        if (string.IsNullOrEmpty(version) && config.IsBuiltIn
            && config.Id.StartsWith("builtin_", StringComparison.Ordinal))
        {
            var serverKey = config.Id["builtin_".Length..];
            version = BuiltInServerHelper.GetInstalledVersion(serverKey);
        }

        if (!string.IsNullOrEmpty(version))
        {
            VersionText.Text = $"v{StripBuildMetadata(version)}";
            VersionText.Visibility = Visibility.Visible;
        }
        else
        {
            VersionText.Visibility = Visibility.Collapsed;
        }

        // Toggle state (suppress event to avoid re-entry)
        _suppressToggleEvent = true;
        EnableToggle.IsOn = config.IsEnabled;
        _suppressToggleEvent = false;

        // Built-in servers: hide edit, delete, toggle, and reconnect (managed per conversation)
        EditButton.Visibility = config.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.Visibility = config.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        EnableToggle.Visibility = config.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        ReconnectButton.Visibility = config.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;

        // Tooltips
        SetMouseToolTip(EditButton, _loader.GetString("Connect_Edit"));
        SetMouseToolTip(ReconnectButton, _loader.GetString("Connect_Reconnect"));
        SetMouseToolTip(DeleteButton, _loader.GetString("Connect_Remove"));
    }

    /// <summary>
    /// Assigns a tooltip that follows the mouse pointer for compact icon buttons.
    /// </summary>
    private static void SetMouseToolTip(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, new ToolTip
        {
            Content = text,
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
        });
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Raises the enable-toggle event after user interaction, unless the control
    /// is currently synchronizing state programmatically.
    /// </summary>
    private void EnableToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleEvent) return;
        if (Connection is not null)
            EnableToggled?.Invoke(this, Connection);
    }

    /// <summary>
    /// Raises the edit request event for the current connection.
    /// </summary>
    private void EditButton_Click(object sender, RoutedEventArgs e)
    {
        if (Connection is not null)
            EditRequested?.Invoke(this, Connection);
    }

    /// <summary>
    /// Raises the reconnect request event for the current connection.
    /// </summary>
    private void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (Connection is not null)
            ReconnectRequested?.Invoke(this, Connection);
    }

    /// <summary>
    /// Raises the delete request event for the current connection.
    /// </summary>
    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (Connection is not null)
            DeleteRequested?.Invoke(this, Connection);
    }

    /// <summary>
    /// Strips the SemVer 2.0 build-metadata suffix (<c>+&lt;sha&gt;</c>) that the
    /// .NET SDK may auto-append to an MCP server's reported version. The commit
    /// hash is diagnostic noise for end users. Well-behaved servers should already
    /// strip this before sending, but we defend defensively here so badly-behaved
    /// or third-party servers still render cleanly in the card.
    /// </summary>
    private static string StripBuildMetadata(string version)
    {
        if (string.IsNullOrEmpty(version)) return version;
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    #endregion
}
