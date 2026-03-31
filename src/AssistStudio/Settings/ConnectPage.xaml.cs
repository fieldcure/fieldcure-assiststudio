using AssistStudio.Controls;
using AssistStudio.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;
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
        var config = await ShowServerDialogAsync(null);
        if (config is null) return;

        _registry?.AddWithoutConnect(config);
        await SaveAndRefreshAsync();
        LoggingService.LogInfo($"[MCP] Server added: {config.Name} ({config.TransportType})");

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

        LoggingService.LogInfo($"[MCP] Toggle: {connection.Config.Name} → {(connection.Config.IsEnabled ? "enabled" : "disabled")}");

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

    private async void OnCardEditRequested(object? sender, McpServerConnection connection)
    {
        if (_registry is null) return;

        var config = connection.Config;
        var updated = await ShowServerDialogAsync(config);
        if (updated is null) return;

        // Detect if restart-requiring fields changed
        var needsRestart = config.Command != updated.Command
            || !ArgsEqual(config.Arguments, updated.Arguments)
            || config.Url != updated.Url;

        // Apply changes to existing config
        config.Description = updated.Description;
        config.Command = updated.Command;
        config.Arguments = updated.Arguments;
        config.Url = updated.Url;

        // Update env vars: clear old vault entries, save new ones
        PasswordVaultHelper.DeleteAllMcpEnvVars(config.Id, config.EnvironmentVariableKeys);
        config.EnvironmentVariables = updated.EnvironmentVariables;
        if (config.EnvironmentVariables is { Count: > 0 })
        {
            config.EnvironmentVariableKeys = [.. config.EnvironmentVariables.Keys];
            PasswordVaultHelper.SaveMcpEnvVars(config.Id, config.EnvironmentVariables);
            needsRestart = true;
        }
        else
        {
            config.EnvironmentVariableKeys = null;
        }

        await SaveAndRefreshAsync();
        LoggingService.LogInfo($"[MCP] Server edited: {config.Name}, needsRestart={needsRestart}");

        // Auto-restart if connected and config changed
        if (needsRestart && connection.IsConnected)
        {
            LoggingService.LogInfo($"[MCP] Restarting after edit: {config.Name}");
            try
            {
                await _registry.ReconnectAsync(connection);
                NotificationCenter.Instance.Post(
                    InfoBarSeverity.Success,
                    string.Format(_loader.GetString("Connect_Restarted"), config.Name),
                    string.Empty);
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[MCP] Restart failed for '{config.Name}': {ex.Message}");
                NotificationCenter.Instance.Post(
                    InfoBarSeverity.Error,
                    _loader.GetString("Connect_RestartFailed"),
                    ex.Message);
            }
            RefreshServerList();
        }
    }

    private async void OnCardReconnectRequested(object? sender, McpServerConnection connection)
    {
        if (_registry is null) return;

        LoggingService.LogInfo($"[MCP] Reconnect requested: {connection.Config.Name}");
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

        var name = connection.Config.Name;
        LoggingService.LogInfo($"[MCP] Delete requested: {name}");

        // Clean up env vars from vault
        PasswordVaultHelper.DeleteAllMcpEnvVars(connection.Config.Id, connection.Config.EnvironmentVariableKeys);

        try
        {
            await _registry.RemoveAsync(connection);
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] Remove failed for '{name}': {ex.Message}");
        }

        await SaveAndRefreshAsync();
        NotificationCenter.Instance.Post(
            InfoBarSeverity.Success,
            string.Format(_loader.GetString("Connect_ServerRemoved"), name),
            string.Empty);
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

        // Filesystem card — standalone placeholder at top
        var fsPrefix = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var activeTabCount = _registry.Connections
            .Count(c => c.Config.Id.StartsWith(fsPrefix, StringComparison.Ordinal) && c.IsConnected);
        var fsPlaceholder = new McpServerConfig
        {
            Id = fsPrefix,
            Name = BuiltInServerHelper.FilesystemDisplayName,
            TransportType = McpTransportType.Stdio,
            Command = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.FilesystemKey),
            IsBuiltIn = true,
            IsEnabled = false,
            Description = activeTabCount > 0
                ? $"{_loader.GetString("Connect_FilesystemActiveInstances")}\n{string.Format(_loader.GetString("Connect_FilesystemActiveCount"), activeTabCount)}"
                : _loader.GetString("Connect_FilesystemNeedsFolders"),
        };
        FilesystemCard.Connection = new McpServerConnection(fsPlaceholder);

        // RAG card — same pattern as Filesystem
        var ragPrefix = $"builtin_{BuiltInServerHelper.RagKey}";
        var ragActiveCount = _registry.Connections
            .Count(c => c.Config.Id.StartsWith(ragPrefix, StringComparison.Ordinal) && c.IsConnected);
        var embeddingModel = AppSettings.EmbeddingModel;
        string ragDescription;
        if (ragActiveCount > 0)
        {
            ragDescription = $"{_loader.GetString("Connect_RagActiveInstances")}\n{string.Format(_loader.GetString("Connect_RagActiveCount"), ragActiveCount)}";
            if (!string.IsNullOrEmpty(embeddingModel))
                ragDescription += $"  ·  {embeddingModel}";
        }
        else if (!string.IsNullOrEmpty(embeddingModel))
        {
            ragDescription = $"{_loader.GetString("Connect_RagNeedsFolders")}\n{embeddingModel}";
        }
        else
        {
            ragDescription = _loader.GetString("Connect_RagNeedsFolders");
        }

        var ragPlaceholder = new McpServerConfig
        {
            Id = ragPrefix,
            Name = BuiltInServerHelper.RagDisplayName,
            TransportType = McpTransportType.Stdio,
            Command = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.RagKey),
            IsBuiltIn = true,
            IsEnabled = false,
            Description = ragDescription,
        };
        RagCard.Connection = new McpServerConnection(ragPlaceholder);

        // Outbox card — shared instance, no folders
        var outboxPrefix = $"builtin_{BuiltInServerHelper.OutboxKey}";
        var outboxConn = _registry.GetBuiltInConnection(BuiltInServerHelper.OutboxKey);
        var outboxPlaceholder = new McpServerConfig
        {
            Id = outboxPrefix,
            Name = BuiltInServerHelper.OutboxDisplayName,
            TransportType = McpTransportType.Stdio,
            Command = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.OutboxKey),
            IsBuiltIn = true,
            IsEnabled = false,
            Description = _loader.GetString("Connect_OutboxDescription"),
        };
        OutboxCard.Connection = new McpServerConnection(outboxPlaceholder);

        // Runner card — shared instance, no folders
        var runnerPrefix = $"builtin_{BuiltInServerHelper.RunnerKey}";
        var runnerConn = _registry.GetBuiltInConnection(BuiltInServerHelper.RunnerKey);
        var runnerPlaceholder = new McpServerConfig
        {
            Id = runnerPrefix,
            Name = BuiltInServerHelper.RunnerDisplayName,
            TransportType = McpTransportType.Stdio,
            Command = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.RunnerKey),
            IsBuiltIn = true,
            IsEnabled = false,
            Description = _loader.GetString("Connect_RunnerDescription"),
        };
        RunnerCard.Connection = new McpServerConnection(runnerPlaceholder);

        // Essentials card — shared instance, no folders
        var essentialsPrefix = $"builtin_{BuiltInServerHelper.EssentialsKey}";
        var essentialsConn = _registry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        var essentialsPlaceholder = new McpServerConfig
        {
            Id = essentialsPrefix,
            Name = BuiltInServerHelper.EssentialsDisplayName,
            TransportType = McpTransportType.Stdio,
            Command = BuiltInServerHelper.GetServerExePath(BuiltInServerHelper.EssentialsKey),
            IsBuiltIn = true,
            IsEnabled = false,
            Description = _loader.GetString("Connect_EssentialsDescription"),
        };
        EssentialsCard.Connection = new McpServerConnection(essentialsPlaceholder);

        // User-configured servers only (exclude all built-in connections)
        var displayList = _registry.Connections
            .Where(c => !c.Config.IsBuiltIn)
            .ToList();

        ServerListControl.ItemsSource = displayList;
        EmptyStateText.Visibility = displayList.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    #endregion

    #region Dialogs

    /// <summary>
    /// Shows the server configuration dialog for adding or editing a server.
    /// When <paramref name="existing"/> is not null, the dialog is in edit mode
    /// with name and transport type read-only.
    /// </summary>
    /// <param name="existing">Existing config to edit, or null for a new server.</param>
    /// <returns>A new config (add) or updated config copy (edit), or null if cancelled.</returns>
    private async Task<McpServerConfig?> ShowServerDialogAsync(McpServerConfig? existing)
    {
        var isEdit = existing is not null;

        var nameBox = new TextBox
        {
            Header = _loader.GetString("Connect_ServerName"),
            PlaceholderText = "e.g., GitHub",
            Text = existing?.Name ?? "",
            IsEnabled = !isEdit,
        };
        var descriptionBox = new TextBox
        {
            Header = _loader.GetString("Connect_ServerDescription"),
            PlaceholderText = "e.g., Manage GitHub repos, issues, and PRs",
            MaxLength = 100,
            TextWrapping = TextWrapping.Wrap,
            Text = existing?.Description ?? "",
        };
        var transportCombo = new ComboBox
        {
            Header = _loader.GetString("Connect_Transport"),
            Items = { "Stdio", "Http" },
            SelectedIndex = existing?.TransportType == McpTransportType.Http ? 1 : 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsEnabled = !isEdit,
        };

        var isStdio = (existing?.TransportType ?? McpTransportType.Stdio) == McpTransportType.Stdio;
        var commandBox = new TextBox
        {
            Header = _loader.GetString("Connect_Command"),
            PlaceholderText = "e.g., npx",
            Text = existing?.Command ?? "",
            Visibility = isStdio ? Visibility.Visible : Visibility.Collapsed,
        };
        var argsBox = new TextBox
        {
            Header = _loader.GetString("Connect_Arguments"),
            PlaceholderText = "e.g., -y @modelcontextprotocol/server-github",
            TextWrapping = TextWrapping.Wrap,
            Text = existing?.Arguments is { Count: > 0 } args ? string.Join(" ", args) : "",
            Visibility = isStdio ? Visibility.Visible : Visibility.Collapsed,
        };
        var urlBox = new TextBox
        {
            Header = _loader.GetString("Connect_Url"),
            PlaceholderText = "e.g., http://localhost:3001/mcp",
            Text = existing?.Url ?? "",
            Visibility = isStdio ? Visibility.Collapsed : Visibility.Visible,
        };

        // Build env vars text from existing config
        var envText = "";
        if (isEdit && existing!.EnvironmentVariableKeys is { Count: > 0 } keys)
        {
            var envVars = PasswordVaultHelper.LoadMcpEnvVars(existing.Id, keys);
            envText = string.Join("\n", envVars.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        var envBox = new TextBox
        {
            Header = _loader.GetString("Connect_EnvVars"),
            PlaceholderText = "GITHUB_TOKEN=ghp_xxx",
            AcceptsReturn = true,
            MinHeight = 60,
            Text = envText,
        };

        // Warning text for built-in command conflict
        var builtInWarning = new TextBlock
        {
            Text = _loader.GetString("Connect_BuiltInDuplicate"),
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 244, 67, 54)),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
        };

        var dialog = new ThemedContentDialog
        {
            Title = isEdit
                ? _loader.GetString("Connect_EditServerDialog")
                : _loader.GetString("Connect_AddServerDialog"),
            PrimaryButtonText = _loader.GetString("Dialog_OK"),
            CloseButtonText = _loader.GetString("Dialog_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        // Real-time validation: required fields + built-in server guard
        if (!isEdit)
        {
            dialog.IsPrimaryButtonEnabled = false;

            void Validate()
            {
                var name = nameBox.Text.Trim();
                var isStdioMode = transportCombo.SelectedIndex == 0;
                var endpoint = isStdioMode ? commandBox.Text.Trim() : urlBox.Text.Trim();

                // Required: name + command/url
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(endpoint))
                {
                    builtInWarning.Visibility = Visibility.Collapsed;
                    dialog.IsPrimaryButtonEnabled = false;
                    return;
                }

                // Built-in server guard
                var isBuiltIn = BuiltInServerHelper.IsBuiltInCommand(name)
                    || BuiltInServerHelper.IsBuiltInCommand(commandBox.Text.Trim());
                builtInWarning.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = !isBuiltIn;
            }

            nameBox.TextChanged += (_, _) => Validate();
            commandBox.TextChanged += (_, _) => Validate();
            urlBox.TextChanged += (_, _) => Validate();

            transportCombo.SelectionChanged += (_, _) =>
            {
                var stdio = transportCombo.SelectedIndex == 0;
                commandBox.Visibility = stdio ? Visibility.Visible : Visibility.Collapsed;
                argsBox.Visibility = stdio ? Visibility.Visible : Visibility.Collapsed;
                urlBox.Visibility = stdio ? Visibility.Collapsed : Visibility.Visible;
                Validate();
            };
        }

        var panel = new StackPanel { Spacing = 12, MinWidth = 400 };
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);
        panel.Children.Add(transportCombo);
        panel.Children.Add(commandBox);
        panel.Children.Add(builtInWarning);
        panel.Children.Add(argsBox);
        panel.Children.Add(urlBox);
        panel.Children.Add(envBox);
        dialog.Content = panel;

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        if (string.IsNullOrWhiteSpace(nameBox.Text))
            return null;

        var config = new McpServerConfig
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N")[..8],
            Name = nameBox.Text.Trim(),
            Description = descriptionBox.Text.Trim(),
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
                if (BuiltInServerHelper.IsBuiltInCommand(config.Command)
                    || BuiltInServerHelper.IsBuiltInCommand(config.Name)) continue;

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

        // Only save user-configured servers (exclude built-in and per-tab connections)
        var configs = _registry.Connections
            .Where(c => !c.Config.IsBuiltIn)
            .Select(c => c.Config)
            .ToList();
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

    private static bool ArgsEqual(List<string>? a, List<string>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.SequenceEqual(b);
    }

    #endregion
}
