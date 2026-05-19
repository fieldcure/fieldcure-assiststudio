using AssistStudio.Controls;
using AssistStudio.Controls.Dialogs;
using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.ApplicationModel.Resources;


#pragma warning disable CS0618 // Obsolete AppSettings.EmbeddingModel — RAG card will be simplified

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

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectPage"/> class.
    /// </summary>
    public ConnectPage()
    {
        InitializeComponent();
    }

    #endregion

    #region Navigation

    /// <inheritdoc/>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _registry = App.McpRegistry;

        // Apply localized strings to code-referenced elements
        AddServerText.Text = _loader.GetString("Connect_AddServer");
        ImportText.Text = _loader.GetString("Connect_ImportFrom");
        EmptyStateText.Text = _loader.GetString("Connect_EmptyState");

        // Surface the ".NET 10 Runtime missing" notice whenever the dnx host failed to
        // initialize. Re-checked on every navigation so users who install the runtime
        // mid-session see the notice disappear after returning to this page.
        DnxNotReadyNotice.IsOpen = !App.IsDnxHostReady;

        // Initialize built-in card sections (created lazily to avoid XAML parse overhead)
        if (_registry is not null)
        {
            var searchSection = new SearchEngineSection();
            searchSection.Initialize(_registry);
            EssentialsCard.BottomContent = searchSection;

            RagCard.BottomContent = new TextBlock
            {
                Text = _loader.GetString("Connect_RagKbPageHint"),
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                Opacity = 0.5,
                TextWrapping = TextWrapping.Wrap,
            };

            var channelsSection = new ChannelsSection();
            channelsSection.Initialize(_registry);
            OutboxCard.BottomContent = channelsSection;
        }

        RefreshServerList();
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Opens the server configuration dialog and adds a new MCP server.
    /// </summary>
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
            // Pre-flight: verify command is resolvable (or offer to install as dotnet tool).
            // Silently skips for non-stdio / known runners / already-on-PATH commands.
            var ready = await McpCommandInstaller.EnsureCommandAvailableAsync(config, XamlRoot);
            if (!ready)
            {
                LoggingService.LogWarning(
                    $"[MCP] Command pre-flight failed for '{config.Name}' (command='{config.Command}'); " +
                    "skipping initial connect.");
                RefreshServerList();
                return;
            }

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

    /// <summary>
    /// Detects importable MCP config sources and shows the import dialog.
    /// </summary>
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

    /// <summary>
    /// Handles enable/disable toggle on an MCP server card.
    /// </summary>
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

    /// <summary>
    /// Opens the edit dialog for an MCP server and applies changes.
    /// </summary>
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

        // Auto-restart if config changed (reconnect even if previously failed)
        if (needsRestart)
        {
            // Pre-flight: the command may have been changed to something uninstalled.
            var ready = await McpCommandInstaller.EnsureCommandAvailableAsync(config, XamlRoot);
            if (!ready)
            {
                LoggingService.LogWarning(
                    $"[MCP] Command pre-flight failed for '{config.Name}' after edit; skipping restart.");
                return;
            }

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

    /// <summary>
    /// Reconnects a disconnected MCP server.
    /// </summary>
    private async void OnCardReconnectRequested(object? sender, McpServerConnection connection)
    {
        if (_registry is null) return;

        LoggingService.LogInfo($"[MCP] Reconnect requested: {connection.Config.Name}");

        // Pre-flight: manual reconnect is a natural recovery point if an earlier spawn
        // failed because the command wasn't installed. Surface the install prompt here
        // so the user can fix it without hunting through the edit dialog.
        var ready = await McpCommandInstaller.EnsureCommandAvailableAsync(connection.Config, XamlRoot);
        if (!ready)
        {
            LoggingService.LogWarning(
                $"[MCP] Command pre-flight failed for '{connection.Config.Name}'; skipping manual reconnect.");
            RefreshServerList();
            return;
        }

        // For built-in servers, refresh dynamic state (provider keys, search engine
        // args, workspace folders) before reconnect so the restarted child process
        // picks up changes made since the first spawn. Matches the "snapshot + refresh"
        // model: the Reconnect button is the explicit refresh trigger.
        if (BuiltInServerHelper.TryRebuildBuiltInConfig(connection.Config))
            LoggingService.LogInfo($"[MCP] Built-in config refreshed before reconnect: {connection.Config.Name}");

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

    /// <summary>
    /// Removes an MCP server and cleans up its vault entries.
    /// </summary>
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

    /// <summary>
    /// Rebuilds the server list UI from the current registry state.
    /// </summary>
    private void RefreshServerList()
    {
        if (_registry is null)
        {
            ServerListControl.ItemsSource = null;
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        // ".NET 10 Runtime missing" affects every dnx-launched server. When that's the case,
        // we render each built-in card as a red-dot Error with a consistent short message
        // (the ConnectPage notice and MainWindow InfoBar carry the install link).
        var dnxNotReadyMessage = !App.IsDnxHostReady
            ? _loader.GetString("Mcp_DnxNotReady_CardError")
            : null;

        // Filesystem card — multi-instance (per-tab), so the card never binds to a single
        // real connection. We synthesize a placeholder connection whose State reflects either
        // the active-instance summary (gray dot) or the dnx precondition error (red dot).
        var fsPrefix = $"builtin_{BuiltInServerHelper.FilesystemKey}";
        var activeTabCount = _registry.Connections
            .Count(c => c.Config.Id.StartsWith(fsPrefix, StringComparison.Ordinal) && c.IsConnected);
        var fsPlaceholder = new McpServerConfig
        {
            Id = fsPrefix,
            Name = BuiltInServerHelper.FilesystemDisplayName,
            TransportType = McpTransportType.Stdio,
            Command = "dnx",
            IsBuiltIn = true,
            IsEnabled = false,
            Description = activeTabCount > 0
                ? $"{_loader.GetString("Connect_FilesystemActiveInstances")}\n{string.Format(_loader.GetString("Connect_FilesystemActiveCount"), activeTabCount)}"
                : _loader.GetString("Connect_FilesystemNeedsFolders"),
        };
        var fsConn = new McpServerConnection(fsPlaceholder);
        if (dnxNotReadyMessage is not null)
            fsConn.SetPlaceholderError(dnxNotReadyMessage);
        FilesystemCard.Connection = fsConn;

        // RAG, Outbox, Runner, Essentials are all *shared* (single instance per app). When
        // a real connection exists in the registry, bind it directly so the card's status
        // dot reflects the live State (Connected / Connecting / Error). When no connection
        // exists (disabled, or RAG with zero KBs), fall back to a descriptive placeholder.
        // The dnx-not-ready override takes precedence over both paths.
        RagCard.Connection = BuildSharedBuiltInConnection(
            BuiltInServerHelper.RagKey,
            BuiltInServerHelper.RagDisplayName,
            BuildRagPlaceholderDescription(),
            dnxNotReadyMessage);

        OutboxCard.Connection = BuildSharedBuiltInConnection(
            BuiltInServerHelper.OutboxKey,
            BuiltInServerHelper.OutboxDisplayName,
            _loader.GetString("Connect_OutboxDescription"),
            dnxNotReadyMessage);

        RunnerCard.Connection = BuildSharedBuiltInConnection(
            BuiltInServerHelper.RunnerKey,
            BuiltInServerHelper.RunnerDisplayName,
            _loader.GetString("Connect_RunnerDescription"),
            dnxNotReadyMessage);

        EssentialsCard.Connection = BuildSharedBuiltInConnection(
            BuiltInServerHelper.EssentialsKey,
            BuiltInServerHelper.EssentialsDisplayName,
            _loader.GetString("Connect_EssentialsDescription"),
            dnxNotReadyMessage);

        // User-configured servers only (exclude all built-in connections)
        var displayList = _registry.Connections
            .Where(c => !c.Config.IsBuiltIn)
            .ToList();

        ServerListControl.ItemsSource = displayList;
        EmptyStateText.Visibility = displayList.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// Returns the live registered <see cref="McpServerConnection"/> for a shared built-in
    /// server when one exists (so its <c>State</c>/<c>ErrorMessage</c> drive the card's
    /// status dot directly). Otherwise synthesizes a disabled placeholder so the card has
    /// something to render; when <paramref name="dnxNotReadyMessage"/> is non-null, the
    /// placeholder is forced into <see cref="McpConnectionState.Error"/> so the card shows
    /// a red dot + that message.
    /// </summary>
    private McpServerConnection BuildSharedBuiltInConnection(
        string serverKey,
        string displayName,
        string fallbackDescription,
        string? dnxNotReadyMessage)
    {
        var real = _registry!.GetBuiltInConnection(serverKey);
        if (real is not null)
            return real;

        var placeholder = new McpServerConnection(new McpServerConfig
        {
            Id = $"builtin_{serverKey}",
            Name = displayName,
            TransportType = McpTransportType.Stdio,
            Command = "dnx",
            IsBuiltIn = true,
            IsEnabled = false,
            Description = fallbackDescription,
        });
        if (dnxNotReadyMessage is not null)
            placeholder.SetPlaceholderError(dnxNotReadyMessage);
        return placeholder;
    }

    /// <summary>
    /// Builds the placeholder description for the RAG card when no shared connection
    /// exists (typically because no Knowledge Base has been created yet). Mirrors the
    /// active-instance / embedding-model formatting the page used before the card was
    /// switched to bind a live connection.
    /// </summary>
    private string BuildRagPlaceholderDescription()
    {
        var ragPrefix = $"builtin_{BuiltInServerHelper.RagKey}";
        var activeCount = _registry!.Connections
            .Count(c => c.Config.Id.StartsWith(ragPrefix, StringComparison.Ordinal) && c.IsConnected);
        var embeddingModel = AppSettings.EmbeddingModel;

        if (activeCount > 0)
        {
            var text = $"{_loader.GetString("Connect_RagActiveInstances")}\n" +
                       string.Format(_loader.GetString("Connect_RagActiveCount"), activeCount);
            if (!string.IsNullOrEmpty(embeddingModel))
                text += $"  ·  {embeddingModel}";
            return text;
        }

        if (!string.IsNullOrEmpty(embeddingModel))
            return $"{_loader.GetString("Connect_RagNeedsFolders")}\n{embeddingModel}";

        return _loader.GetString("Connect_RagNeedsFolders");
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
            TextWrapping = TextWrapping.Wrap,
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

                // Built-in server guard — check both the name and the command,
                // passing the args so dnx-launched built-ins are matched on package id.
                var cmdArgs = argsBox.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var isBuiltIn = BuiltInServerHelper.IsBuiltInCommand(name)
                    || BuiltInServerHelper.IsBuiltInCommand(commandBox.Text.Trim(), cmdArgs);
                builtInWarning.Visibility = isBuiltIn ? Visibility.Visible : Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = !isBuiltIn;
            }

            nameBox.TextChanged += (_, _) => Validate();
            commandBox.TextChanged += (_, _) => Validate();
            urlBox.TextChanged += (_, _) => Validate();
        }

        var commandHint = new TextBlock
        {
            Text = _loader.GetString("Connect_CommandHint"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, -8, 0, 0),
        };

        transportCombo.SelectionChanged += (_, _) =>
        {
            var stdio = transportCombo.SelectedIndex == 0;
            commandBox.Visibility = stdio ? Visibility.Visible : Visibility.Collapsed;
            commandHint.Visibility = stdio ? Visibility.Visible : Visibility.Collapsed;
            argsBox.Visibility = stdio ? Visibility.Visible : Visibility.Collapsed;
            urlBox.Visibility = stdio ? Visibility.Collapsed : Visibility.Visible;
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 400, MaxWidth = 500 };
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);
        panel.Children.Add(transportCombo);
        panel.Children.Add(commandBox);
        panel.Children.Add(commandHint);
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

        // Parse env vars. Split on BOTH '\r' and '\n': a multi-line WinUI TextBox
        // (AcceptsReturn=true) reports its line breaks as bare '\r' (U+000D), not
        // '\n', so splitting on '\n' alone collapses every line into one entry —
        // the dialog would then set only the first KEY with a garbage value
        // containing the rest. RemoveEmptyEntries also absorbs the empty token a
        // "\r\n" pair would otherwise produce.
        if (!string.IsNullOrWhiteSpace(envBox.Text))
        {
            config.EnvironmentVariables = [];
            foreach (var line in envBox.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    config.EnvironmentVariables[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
        }

        return config;
    }

    /// <summary>
    /// Shows a dialog for selecting which external MCP configs to import.
    /// </summary>
    private async Task ShowImportDialogAsync(IReadOnlyList<ImportSource> sources)
    {
        var serversLabel = _loader.GetString("Connect_Servers");
        var panel = new StackPanel { Spacing = 12, MinWidth = 400, MaxWidth = 500 };
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
                if (BuiltInServerHelper.IsBuiltInCommand(config.Command, config.Arguments)
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

    /// <summary>
    /// Persists current MCP server configs and refreshes the UI.
    /// </summary>
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

    /// <summary>
    /// Connects all enabled but disconnected servers in the background.
    /// </summary>
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

    /// <summary>
    /// Compares two argument lists for equality.
    /// </summary>
    private static bool ArgsEqual(List<string>? a, List<string>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.SequenceEqual(b);
    }

    #endregion
}
