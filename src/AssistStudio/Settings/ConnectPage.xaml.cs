using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Settings;

/// <summary>
/// Settings page for managing MCP server connections.
/// Allows users to add, edit, delete, and import MCP server configurations.
/// </summary>
public sealed partial class ConnectPage : Page
{
    #region Fields

    private McpServerRegistry? _registry;
    private readonly ResourceLoader _loader = new();

    #endregion

    #region Constructor

    public ConnectPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Navigation

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _registry = App.McpRegistry;

        // Apply localized strings to code-referenced elements
        AddServerText.Text = _loader.GetString("Connect_AddServer");
        ImportText.Text = _loader.GetString("Connect_ImportFrom");
        EmptyStateText.Text = _loader.GetString("Connect_EmptyState");

        RefreshServerList();
    }

    #endregion

    #region Event Handlers

    private async void AddServerButton_Click(object sender, RoutedEventArgs e)
    {
        var config = await ShowAddServerDialogAsync();
        if (config is null) return;

        _registry?.AddWithoutConnect(config);
        await SaveAndRefreshAsync();

        // Connect in background
        if (config.IsEnabled && _registry is not null)
        {
            try
            {
                var connection = _registry.Connections.FirstOrDefault(c => c.Config.Id == config.Id);
                if (connection is not null)
                    await _registry.ReconnectAsync(connection);
            }
            catch { /* Error state shown in UI */ }

            RefreshServerList();
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var sources = McpConfigImporter.DetectSources();
        if (sources.Count == 0)
        {
            await ShowMessageAsync(
                _loader.GetString("Connect_NoSourcesFound"),
                _loader.GetString("Connect_NoSourcesMessage"));
            return;
        }

        await ShowImportDialogAsync(sources);
    }

    private async void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: McpServerConnection connection }) return;
        if (_registry is null) return;

        try
        {
            await _registry.ReconnectAsync(connection);
        }
        catch { /* Error state shown in UI */ }

        RefreshServerList();
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: McpServerConnection connection }) return;
        if (_registry is null) return;

        // Clean up env vars from vault
        PasswordVaultHelper.DeleteAllMcpEnvVars(connection.Config.Id, connection.Config.EnvironmentVariableKeys);

        await _registry.RemoveAsync(connection);
        await SaveAndRefreshAsync();
    }

    private async void EnableToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch { Tag: McpServerConnection connection }) return;
        if (_registry is null) return;

        connection.Config.IsEnabled = ((ToggleSwitch)sender).IsOn;

        if (connection.Config.IsEnabled && !connection.IsConnected)
        {
            try { await _registry.ReconnectAsync(connection); }
            catch { /* shown in UI */ }
        }
        else if (!connection.Config.IsEnabled && connection.IsConnected)
        {
            await connection.DisconnectAsync();
        }

        await SaveAndRefreshAsync();
    }

    #endregion

    #region UI Building

    private void RefreshServerList()
    {
        ServerListPanel.Children.Clear();

        if (_registry is null || _registry.Connections.Count == 0)
        {
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;

        foreach (var connection in _registry.Connections)
        {
            ServerListPanel.Children.Add(BuildServerCard(connection));
        }
    }

    private Border BuildServerCard(McpServerConnection connection)
    {
        var config = connection.Config;

        // Status indicator
        var (statusGlyph, statusColor) = connection.State switch
        {
            McpConnectionState.Connected => ("\uF136", "#4CAF50"),     // Filled circle green
            McpConnectionState.Connecting => ("\uF136", "#FFC107"),    // Yellow
            McpConnectionState.Error => ("\uF136", "#F44336"),        // Red
            _ => ("\uF136", "#9E9E9E"),                               // Gray
        };

        var statusIcon = new FontIcon
        {
            Glyph = statusGlyph,
            FontSize = 10,
            Foreground = new SolidColorBrush(ParseColor(statusColor)),
            VerticalAlignment = VerticalAlignment.Center,
        };

        // Server name + transport badge
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        namePanel.Children.Add(statusIcon);
        namePanel.Children.Add(new TextBlock
        {
            Text = config.Name,
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
            VerticalAlignment = VerticalAlignment.Center,
        });
        namePanel.Children.Add(new Border
        {
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Child = new TextBlock
            {
                Text = config.TransportType.ToString(),
                FontSize = 11,
                Opacity = 0.7,
            },
        });

        // Details: command/url + tool count
        var detailText = config.TransportType == McpTransportType.Stdio
            ? $"{config.Command} {string.Join(" ", config.Arguments ?? [])}"
            : config.Url ?? "";

        var toolsLabel = _loader.GetString("Connect_Tools");
        var toolCount = connection.IsConnected ? $" · {connection.Tools.Count} {toolsLabel}" : "";
        var errorText = connection.State == McpConnectionState.Error ? $" · {connection.ErrorMessage}" : "";

        var detailPanel = new TextBlock
        {
            Text = detailText + toolCount + errorText,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 500,
        };

        // Action buttons
        var actionsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var enableToggle = new ToggleSwitch
        {
            IsOn = config.IsEnabled,
            Tag = connection,
            OnContent = "",
            OffContent = "",
            MinWidth = 0,
        };
        enableToggle.Toggled += EnableToggle_Toggled;

        var reconnectBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(6),
            Tag = connection,
        };
        ToolTipService.SetToolTip(reconnectBtn, _loader.GetString("Connect_Reconnect"));
        reconnectBtn.Click += ReconnectButton_Click;

        var deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 },
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(6),
            Tag = connection,
        };
        ToolTipService.SetToolTip(deleteBtn, _loader.GetString("Connect_Remove"));
        deleteBtn.Click += DeleteButton_Click;

        actionsPanel.Children.Add(enableToggle);
        actionsPanel.Children.Add(reconnectBtn);
        actionsPanel.Children.Add(deleteBtn);

        // Layout: left info + right actions
        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var infoPanel = new StackPanel { Spacing = 4 };
        infoPanel.Children.Add(namePanel);
        infoPanel.Children.Add(detailPanel);

        Grid.SetColumn(infoPanel, 0);
        Grid.SetColumn(actionsPanel, 1);
        headerRow.Children.Add(infoPanel);
        headerRow.Children.Add(actionsPanel);

        // Card border
        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = headerRow,
        };
    }

    #endregion

    #region Dialogs

    private async Task<McpServerConfig?> ShowAddServerDialogAsync()
    {
        var nameBox = new TextBox { Header = _loader.GetString("Connect_ServerName"), PlaceholderText = "e.g., GitHub" };
        var transportCombo = new ComboBox
        {
            Header = _loader.GetString("Connect_Transport"),
            Items = { "Stdio", "Http" },
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var commandBox = new TextBox { Header = _loader.GetString("Connect_Command"), PlaceholderText = "e.g., npx" };
        var argsBox = new TextBox { Header = _loader.GetString("Connect_Arguments"), PlaceholderText = "e.g., -y @modelcontextprotocol/server-github" };
        var urlBox = new TextBox { Header = _loader.GetString("Connect_Url"), PlaceholderText = "e.g., http://localhost:3001/mcp", Visibility = Visibility.Collapsed };
        var envBox = new TextBox
        {
            Header = _loader.GetString("Connect_EnvVars"),
            PlaceholderText = "GITHUB_TOKEN=ghp_xxx",
            AcceptsReturn = true,
            MinHeight = 60,
        };

        transportCombo.SelectionChanged += (_, _) =>
        {
            var isStdio = transportCombo.SelectedIndex == 0;
            commandBox.Visibility = isStdio ? Visibility.Visible : Visibility.Collapsed;
            argsBox.Visibility = isStdio ? Visibility.Visible : Visibility.Collapsed;
            urlBox.Visibility = isStdio ? Visibility.Collapsed : Visibility.Visible;
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 400 };
        panel.Children.Add(nameBox);
        panel.Children.Add(transportCombo);
        panel.Children.Add(commandBox);
        panel.Children.Add(argsBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(envBox);

        var dialog = new ContentDialog
        {
            Title = _loader.GetString("Connect_AddServerDialog"),
            Content = panel,
            PrimaryButtonText = _loader.GetString("Dialog_OK"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        if (string.IsNullOrWhiteSpace(nameBox.Text))
            return null;

        var config = new McpServerConfig
        {
            Name = nameBox.Text.Trim(),
            TransportType = transportCombo.SelectedIndex == 0 ? McpTransportType.Stdio : McpTransportType.Http,
        };

        if (config.TransportType == McpTransportType.Stdio)
        {
            config.Command = commandBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(argsBox.Text))
                config.Arguments = [.. argsBox.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)];
        }
        else
        {
            config.Url = urlBox.Text.Trim();
        }

        // Parse env vars
        if (!string.IsNullOrWhiteSpace(envBox.Text))
        {
            config.EnvironmentVariables = [];
            foreach (var line in envBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    config.EnvironmentVariables[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }

        return config;
    }

    private async Task ShowImportDialogAsync(IReadOnlyList<ImportSource> sources)
    {
        var serversLabel = _loader.GetString("Connect_Servers");
        var panel = new StackPanel { Spacing = 12, MinWidth = 400 };
        var checkBoxes = new List<(CheckBox Check, ImportSource Source)>();

        foreach (var source in sources)
        {
            var cb = new CheckBox
            {
                Content = $"{source.AppName} ({source.ServerCount} {serversLabel})",
                IsChecked = true,
            };
            checkBoxes.Add((cb, source));
            panel.Children.Add(cb);
        }

        var dialog = new ContentDialog
        {
            Title = _loader.GetString("Connect_ImportDialog"),
            Content = panel,
            PrimaryButtonText = _loader.GetString("Dialog_OK"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var existingNames = _registry?.Connections.Select(c => c.Config.Name).ToHashSet() ?? [];
        var imported = 0;

        foreach (var (cb, source) in checkBoxes)
        {
            if (cb.IsChecked != true) continue;

            var configs = McpConfigImporter.ParseFrom(source);
            foreach (var config in configs)
            {
                if (existingNames.Contains(config.Name)) continue;

                _registry?.AddWithoutConnect(config);
                existingNames.Add(config.Name);
                imported++;
            }
        }

        if (imported > 0)
        {
            await SaveAndRefreshAsync();

            // Connect imported servers in background
            _ = ConnectPendingServersAsync();
        }

        await ShowMessageAsync(
            _loader.GetString("Connect_ImportComplete"),
            string.Format(_loader.GetString("Connect_ImportedCount"), imported));
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };
        await dialog.ShowAsync();
    }

    #endregion

    #region Private Methods

    private async Task SaveAndRefreshAsync()
    {
        if (_registry is null) return;

        var configs = _registry.Connections.Select(c => c.Config).ToList();
        await AppSettings.SaveMcpServersAsync(configs);
        RefreshServerList();
    }

    private async Task ConnectPendingServersAsync()
    {
        if (_registry is null) return;

        foreach (var conn in _registry.Connections.Where(c => c.Config.IsEnabled && !c.IsConnected && c.State != McpConnectionState.Connecting))
        {
            try { await _registry.ReconnectAsync(conn); }
            catch { /* shown in UI */ }
        }

        DispatcherQueue.TryEnqueue(RefreshServerList);
    }

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(255,
            byte.Parse(hex[..2], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber),
            byte.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber));
    }

    #endregion
}
