using AssistStudio.Controls;
using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            catch (Exception ex)
            {
                LoggingService.LogError($"[MCP] Failed to connect server '{config.Name}': {ex.Message}");
            }

            RefreshServerList();
        }
    }

    private async void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        var sources = McpConfigImporter.DetectSources();
        if (sources.Count == 0)
        {
            NotificationCenter.Instance.Post(
                Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning,
                _loader.GetString("Connect_NoSourcesFound"),
                _loader.GetString("Connect_NoSourcesMessage"),
                4000);
            return;
        }

        await ShowImportDialogAsync(sources);
    }

    private async void OnCardEnableToggled(object? sender, McpServerConnection connection)
    {
        if (_registry is null) return;

        if (sender is McpServerCard card)
            connection.Config.IsEnabled = card.IsToggleOn;

        if (connection.Config.IsEnabled && !connection.IsConnected)
        {
            try { await _registry.ReconnectAsync(connection); }
            catch (Exception ex)
            {
                LoggingService.LogError($"[MCP] Reconnect failed for '{connection.Config.Name}': {ex.Message}");
            }
        }
        else if (!connection.Config.IsEnabled && connection.IsConnected)
        {
            try { await connection.DisconnectAsync(); }
            catch (Exception ex)
            {
                LoggingService.LogError($"[MCP] Disconnect failed for '{connection.Config.Name}': {ex.Message}");
            }
        }

        await SaveAndRefreshAsync();
    }

    private async void OnCardReconnectRequested(object? sender, McpServerConnection connection)
    {
        if (_registry is null) return;

        try
        {
            await _registry.ReconnectAsync(connection);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] Reconnect failed for '{connection.Config.Name}': {ex.Message}");
        }

        RefreshServerList();
    }

    private async void OnCardDeleteRequested(object? sender, McpServerConnection connection)
    {
        if (_registry is null) return;

        // Clean up env vars from vault
        PasswordVaultHelper.DeleteAllMcpEnvVars(connection.Config.Id, connection.Config.EnvironmentVariableKeys);

        try
        {
            await _registry.RemoveAsync(connection);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] Remove failed for '{connection.Config.Name}': {ex.Message}");
        }

        await SaveAndRefreshAsync();
    }

    #endregion

    #region UI Building

    private void RefreshServerList()
    {
        if (_registry is null)
        {
            ServerListControl.ItemsSource = null;
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        ServerListControl.ItemsSource = _registry.Connections;
        EmptyStateText.Visibility = _registry.Connections.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        var dialog = new ThemedContentDialog
        {
            Title = _loader.GetString("Connect_AddServerDialog"),
            Content = panel,
            PrimaryButtonText = _loader.GetString("Dialog_OK"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,

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

        var dialog = new ThemedContentDialog
        {
            Title = _loader.GetString("Connect_ImportDialog"),
            Content = panel,
            PrimaryButtonText = _loader.GetString("Dialog_OK"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,

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

        NotificationCenter.Instance.Post(
            _loader.GetString("Connect_ImportComplete"),
            string.Format(_loader.GetString("Connect_ImportedCount"), imported));
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
            catch (Exception ex)
            {
                LoggingService.LogError($"[MCP] Background connect failed for '{conn.Config.Name}': {ex.Message}");
            }
        }

        DispatcherQueue.TryEnqueue(RefreshServerList);
    }

    #endregion
}
