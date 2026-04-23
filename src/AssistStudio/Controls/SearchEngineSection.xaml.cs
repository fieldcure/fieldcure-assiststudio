using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Text.Json;

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section for selecting the Essentials MCP search engine and managing API keys.
/// Designed to be injected into the EssentialsCard's BottomContent slot.
/// </summary>
public sealed partial class SearchEngineSection : UserControl
{
    #region Constants

    /// <summary>
    /// ServerId used for the shared McpEnv_{serverId}_{key} credential slot.
    /// Must match <c>BuiltInServerHelper.CreateMcpServerConfig</c> (builtin_essentials)
    /// and Runner's auto-detected Essentials id so host-entered keys reach Runner-spawned
    /// Essentials without any mirror step (ADR-001).
    /// </summary>
    private const string EssentialsServerId = "builtin_essentials";

    private const string SerperEnvKey = "SERPER_API_KEY";
    private const string TavilyEnvKey = "TAVILY_API_KEY";
    private const string SerpApiEnvKey = "SERPAPI_API_KEY";
    private const string WolframEnvKey = "WOLFRAM_APPID";

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private McpServerRegistry? _registry;
    private bool _loaded;

    /// <summary>
    /// Guards <see cref="OnSearchEngineChanged"/> while the UI is being
    /// programmatically synced to the live server state via
    /// <c>get_search_engine</c>. The checked-radio event still fires, but
    /// the handler must not persist to AppSettings or re-invoke the server
    /// — the value it's applying came from the server in the first place.
    /// </summary>
    private bool _isSyncingFromServer;

    #endregion

    #region Constructor

    public SearchEngineSection()
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
        Section.Header = _loader.GetString("Connect_SearchEngine");
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles the section expander being opened for the first time, triggering
    /// lazy UI construction and (when Essentials is already connected) a
    /// one-shot sync of the radio selection to the server's live engine. The
    /// server's state can drift from AppSettings after a mid-conversation
    /// <c>set_search_engine</c> call, and the saved value is only the
    /// next-restart default — without this sync the UI would misrepresent
    /// what search actually happens when the user triggers a tool.
    /// </summary>
    private void OnSectionExpanded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        BuildUI();
        _ = SyncFromServerAsync();
    }

    /// <summary>
    /// Handles search engine radio button selection changes: persists the choice
    /// and applies it to the running Essentials server without a restart when
    /// possible. Since v2.3.0 Essentials exposes a <c>set_search_engine</c> MCP
    /// tool that swaps the active engine in place; restarting the server just
    /// to change the initial CLI <c>--search-engine</c> argument is only needed
    /// when the runtime swap is unavailable (server not connected, tool call
    /// fails, or the selection is the free default which has no paid fallback
    /// considerations).
    /// </summary>
    private void OnSearchEngineChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio || radio.Tag is not string engineKey) return;

        // Skip persistence + server call when this event was fired by the
        // programmatic sync-from-server path: the new value came from the
        // server, so writing AppSettings would overwrite the user's saved
        // default with a transient runtime state, and calling set_search_engine
        // back at the server would be a no-op echo.
        if (_isSyncingFromServer) return;

        var configs = AppSettings.BuiltInServers;
        if (!configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config))
        {
            config = new BuiltInServerConfig { IsEnabled = true };
            configs[BuiltInServerHelper.EssentialsKey] = config;
        }

        config.SearchEngine = engineKey == "default" ? null : engineKey;
        AppSettings.BuiltInServers = configs;
        LoggingService.LogInfo($"[MCP] Search engine changed to: {engineKey}");

        _ = ApplyEngineChangeAsync(engineKey);
    }

    /// <summary>
    /// Persists the API key, enables/selects the radio button, and shows/hides the trash button.
    /// </summary>
    private void OnApiKeyChanged(object sender, RadioButton radio, Button clearButton, string engineKey)
    {
        if (sender is not PasswordBox pb || pb.Tag is not string envKey) return;

        var value = pb.Password?.Trim() ?? "";
        var hasKey = !string.IsNullOrEmpty(value);

        if (hasKey)
        {
            PasswordVaultHelper.SaveMcpEnvVar(EssentialsServerId, envKey, value);
            radio.IsEnabled = true;
            radio.IsChecked = true; // auto-select on key entry
            clearButton.Visibility = Visibility.Visible;
            LoggingService.LogInfo($"[MCP] API key saved for {engineKey}, auto-selected");
        }
        else
        {
            PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, envKey);
            radio.IsEnabled = false;
            radio.IsChecked = false;
            clearButton.Visibility = Visibility.Collapsed;
            LoggingService.LogInfo($"[MCP] API key removed for {engineKey}, radio disabled");

            // Fall back to free engine if this was the selected one
            if (engineKey == GetCurrentEngine())
            {
                LoggingService.LogInfo($"[MCP] Active engine '{engineKey}' lost API key, falling back to default");
                SelectFreeEngine();
            }
        }
    }

    /// <summary>
    /// Clears the API key from the password box and PasswordVault, disables the radio button.
    /// </summary>
    private void OnClearApiKey(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not PasswordBox pb) return;

        pb.Password = "";
        if (pb.Tag is string envKey)
        {
            PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, envKey);
            LoggingService.LogInfo($"[MCP] API key cleared: {envKey}");
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Returns the currently configured search engine key from AppSettings.
    /// </summary>
    private static string GetCurrentEngine()
    {
        var configs = AppSettings.BuiltInServers;
        configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config);
        return config?.SearchEngine ?? "default";
    }

    /// <summary>
    /// Selects the free engine (Bing/DuckDuckGo) radio button and persists the choice.
    /// </summary>
    private void SelectFreeEngine()
    {
        // Find the free radio button (Tag == "default") in ContentPanel
        foreach (var child in ContentPanel.Children)
        {
            if (child is RadioButton rb && rb.Tag is string tag && tag == "default")
            {
                rb.IsChecked = true; // triggers OnSearchEngineChanged
                return;
            }
        }
    }

    /// <summary>
    /// Constructs the search engine selection UI with free and paid engine radio buttons.
    /// </summary>
    private void BuildUI()
    {
        var panel = ContentPanel;
        panel.Children.Clear();

        var configs = AppSettings.BuiltInServers;
        configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var essentialsConfig);
        var rawEngine = essentialsConfig?.SearchEngine;
        var currentEngine = string.IsNullOrEmpty(rawEngine) ? "default" : rawEngine;

        // Validate: if a paid engine is selected but its API key is missing, fall back to default
        if (currentEngine is not "default")
        {
            var envKey = currentEngine switch
            {
                "serper" => SerperEnvKey,
                "tavily" => TavilyEnvKey,
                "serpapi" => SerpApiEnvKey,
                _ => null,
            };
            if (envKey is null || string.IsNullOrEmpty(PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, envKey)))
            {
                LoggingService.LogInfo($"[MCP] Search engine '{currentEngine}' has no API key, resetting to default (Bing/DuckDuckGo)");
                currentEngine = "default";
                if (essentialsConfig is not null)
                {
                    essentialsConfig.SearchEngine = null;
                    AppSettings.BuiltInServers = configs;
                    LoggingService.LogInfo("[MCP] Reconnecting Essentials with default search engine");
                    _ = ReconnectAsync();
                }
            }
        }

        var freeHint = _loader.GetString("Connect_SearchEngineFreeHint");
        var apiKeyPlaceholder = _loader.GetString("Connect_ApiKeyPlaceholder") ?? "API key";

        // Runtime-swap hint — explains why the radio selection may drift during
        // conversations (tools can change the engine in place) and how restart
        // behaviour relates to the saved configuration.
        var runtimeHintText = _loader.GetString("Connect_SearchEngineRuntimeHint");
        if (!string.IsNullOrEmpty(runtimeHintText))
        {
            var hint = new TextBlock
            {
                Text = runtimeHintText,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.7,
                Margin = new Thickness(0, 0, 0, 8),
            };
            panel.Children.Add(hint);
        }

        // Bing / DuckDuckGo (free)
        var freeRadio = new RadioButton
        {
            GroupName = "SearchEngine",
            Tag = "default",
            IsChecked = currentEngine is "default" or "" or null,
            Margin = new Thickness(0),
        };
        var freeContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        freeContent.Children.Add(new TextBlock
        {
            Text = _loader.GetString("Connect_SearchEngineFree"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        freeContent.Children.Add(new TextBlock
        {
            Text = freeHint,
            Opacity = 0.5,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });
        freeRadio.Content = freeContent;
        freeRadio.Checked += OnSearchEngineChanged;
        panel.Children.Add(freeRadio);

        // Paid engines
        AddPaidEngineRow(panel, "Serper", "serper", SerperEnvKey, currentEngine!, apiKeyPlaceholder);
        AddPaidEngineRow(panel, "Tavily", "tavily", TavilyEnvKey, currentEngine!, apiKeyPlaceholder);
        AddPaidEngineRow(panel, "SerpApi", "serpapi", SerpApiEnvKey, currentEngine!, apiKeyPlaceholder);

        // Wolfram|Alpha — independent optional capability, not part of the search engine group
        AddWolframRow(panel);
    }

    /// <summary>
    /// Adds a Wolfram|Alpha AppID input row. Not part of the search engine radio group —
    /// it's an independent optional capability. Reconnects the Essentials server on change
    /// so the <c>WOLFRAM_APPID</c> env var is picked up.
    /// </summary>
    private void AddWolframRow(StackPanel parent)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var existingKey = PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, WolframEnvKey);
        var hasKey = !string.IsNullOrEmpty(existingKey);

        var label = new TextBlock
        {
            Text = _loader.GetString("Connect_WolframAlpha"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var clearButton = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed,
        };
        clearButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
        clearButton.Click += OnClearApiKey;
        ToolTipService.SetToolTip(clearButton, new ToolTip
        {
            Content = _loader.GetString("Connect_Remove"),
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
        });
        Grid.SetColumn(clearButton, 2);
        row.Children.Add(clearButton);

        var inputStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        var passwordBox = new PasswordBox
        {
            PlaceholderText = _loader.GetString("Connect_WolframAppIdPlaceholder") ?? "AppID",
            Password = hasKey ? existingKey : "",
            PasswordRevealMode = PasswordRevealMode.Hidden,
            Width = 220,
            Tag = WolframEnvKey,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        passwordBox.PasswordChanged += OnWolframAppIdChanged;
        inputStack.Children.Add(passwordBox);

        inputStack.Children.Add(new TextBlock
        {
            Text = _loader.GetString("Connect_WolframAppIdHint"),
            Opacity = 0.5,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });

        Grid.SetColumn(inputStack, 1);
        row.Children.Add(inputStack);

        clearButton.Tag = passwordBox;

        parent.Children.Add(row);
    }

    /// <summary>
    /// Persists the Wolfram|Alpha AppID to PasswordVault and reconnects Essentials so the
    /// new <c>WOLFRAM_APPID</c> env var takes effect.
    /// </summary>
    private void OnWolframAppIdChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;

        // Locate the sibling clear button: PasswordBox → StackPanel → Grid row
        Button? clearButton = null;
        if (pb.Parent is FrameworkElement fe && fe.Parent is Grid grid)
        {
            foreach (var child in grid.Children)
            {
                if (child is Button b) { clearButton = b; break; }
            }
        }

        var value = pb.Password?.Trim() ?? "";
        var hasKey = !string.IsNullOrEmpty(value);

        if (hasKey)
        {
            PasswordVaultHelper.SaveMcpEnvVar(EssentialsServerId, WolframEnvKey, value);
            if (clearButton is not null) clearButton.Visibility = Visibility.Visible;
            LoggingService.LogInfo("[MCP] Wolfram|Alpha AppID saved");
        }
        else
        {
            PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, WolframEnvKey);
            if (clearButton is not null) clearButton.Visibility = Visibility.Collapsed;
            LoggingService.LogInfo("[MCP] Wolfram|Alpha AppID removed");
        }

        _ = ReconnectAsync();
    }

    /// <summary>
    /// Adds a paid search engine row with radio button (disabled until key entered),
    /// API key input (reveal disabled), and trash button (hidden until key exists).
    /// </summary>
    private void AddPaidEngineRow(
        StackPanel parent, string displayName, string engineKey, string envKey,
        string currentEngine, string apiKeyPlaceholder)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var existingKey = PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, envKey);
        var hasKey = !string.IsNullOrEmpty(existingKey);

        var radio = new RadioButton
        {
            Content = displayName,
            GroupName = "SearchEngine",
            Tag = engineKey,
            IsChecked = currentEngine == engineKey,
            IsEnabled = hasKey,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0),
        };
        radio.Checked += OnSearchEngineChanged;
        Grid.SetColumn(radio, 0);
        row.Children.Add(radio);

        var clearButton = new Button
        {
            Style = (Style)Application.Current.Resources["SubtleButtonStyle"],
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
            Visibility = hasKey ? Visibility.Visible : Visibility.Collapsed,
        };
        clearButton.Content = new FontIcon { Glyph = "\uE74D", FontSize = 14 };
        clearButton.Click += OnClearApiKey;
        ToolTipService.SetToolTip(clearButton, new ToolTip
        {
            Content = _loader.GetString("Connect_Remove"),
            Placement = Microsoft.UI.Xaml.Controls.Primitives.PlacementMode.Mouse,
        });
        Grid.SetColumn(clearButton, 2);
        row.Children.Add(clearButton);

        var passwordBox = new PasswordBox
        {
            PlaceholderText = apiKeyPlaceholder,
            Password = hasKey ? existingKey : "",
            PasswordRevealMode = PasswordRevealMode.Hidden,
            Width = 220,
            Tag = envKey,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Store references for cross-updates: Tag holds envKey, but we need radio + clearButton too
        passwordBox.PasswordChanged += (s, _) => OnApiKeyChanged(s, radio, clearButton, engineKey);
        Grid.SetColumn(passwordBox, 1);
        row.Children.Add(passwordBox);

        // Wire clear button to password box
        clearButton.Tag = passwordBox;

        parent.Children.Add(row);
    }

    /// <summary>
    /// Queries the live Essentials server's <c>get_search_engine</c> tool
    /// (v2.4.0+) and flips the matching radio button so the UI reflects the
    /// actual engine in use. Silently no-ops when the server is not
    /// connected, the tool is unavailable (older Essentials), or the call
    /// fails — in those cases the radio stays on the AppSettings-derived
    /// default that <see cref="BuildUI"/> already selected, which is the
    /// closest approximation we have without a live read.
    /// </summary>
    private async Task SyncFromServerAsync()
    {
        if (_registry is null) return;

        var conn = _registry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (conn is null || !conn.IsConnected) return;
        if (!conn.Tools.Any(t => t.Name == "get_search_engine")) return;

        try
        {
            using var argsDoc = JsonDocument.Parse("{}");
            var resultJson = await conn.CallToolWithProgressAsync(
                "get_search_engine",
                argsDoc.RootElement,
                progress: null,
                ct: CancellationToken.None);

            using var parsed = JsonDocument.Parse(resultJson);
            if (!parsed.RootElement.TryGetProperty("key", out var keyProp)
                || keyProp.ValueKind != JsonValueKind.String)
            {
                LoggingService.LogWarning("[MCP] get_search_engine returned no 'key' field; skipping sync");
                return;
            }

            var serverKey = keyProp.GetString();
            if (string.IsNullOrEmpty(serverKey)) return;

            // The "default" UI option has no server-side equivalent — Essentials
            // always reports a concrete engine key. Map bing back to "default"
            // only when the user has not explicitly saved a paid engine, so a
            // user who chose Bing and a fallback-produced Bing look the same.
            var targetTag = ResolveRadioTagForServerKey(serverKey);
            var radio = FindRadioByTag(targetTag);
            if (radio is null)
            {
                LoggingService.LogInfo($"[MCP] get_search_engine reported '{serverKey}' but no matching radio; leaving UI as-is");
                return;
            }
            if (radio.IsChecked == true) return;

            _isSyncingFromServer = true;
            try
            {
                radio.IsChecked = true;
            }
            finally
            {
                _isSyncingFromServer = false;
            }

            LoggingService.LogInfo($"[MCP] Search engine UI synced to server state: {serverKey} (radio='{targetTag}')");
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[MCP] get_search_engine call failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps the canonical engine key returned by <c>get_search_engine</c> to
    /// the radio button tag used in <see cref="BuildUI"/>. When the server
    /// reports <c>bing</c> and the user has not saved a paid engine, the
    /// free-default radio (<c>"default"</c>) is selected so the Bing/DDG
    /// fallback and an explicit Bing choice look identical in the UI.
    /// </summary>
    /// <param name="serverKey">Lowercase canonical key from <c>get_search_engine</c>.</param>
    /// <returns>The radio tag to check, or the server key itself for paid engines.</returns>
    private static string ResolveRadioTagForServerKey(string serverKey)
    {
        var saved = GetCurrentEngine();
        if (string.Equals(serverKey, "bing", StringComparison.OrdinalIgnoreCase)
            && saved == "default")
        {
            return "default";
        }
        return serverKey;
    }

    /// <summary>
    /// Finds the <see cref="RadioButton"/> in <c>ContentPanel</c> whose
    /// <see cref="FrameworkElement.Tag"/> matches <paramref name="tag"/>
    /// (case-insensitive), or <see langword="null"/> when no match exists.
    /// </summary>
    private RadioButton? FindRadioByTag(string tag)
    {
        foreach (var child in ContentPanel.Children)
        {
            if (child is RadioButton rb
                && rb.Tag is string rbTag
                && string.Equals(rbTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return rb;
            }
        }
        return null;
    }

    /// <summary>
    /// Applies an engine change by calling the live Essentials server's
    /// <c>set_search_engine</c> tool (v2.3.0+) instead of restarting the
    /// process. Falls back to a full reconnect when the server is not
    /// connected, the tool is unavailable, or the tool call fails —
    /// preserving correct behaviour for older Essentials builds that do not
    /// expose the runtime-switch tool.
    /// </summary>
    /// <param name="engineKey">
    /// The engine key selected in the UI (<c>"default"</c> for the free
    /// Bing/DuckDuckGo fallback, or a paid-engine key like <c>"serper"</c>).
    /// </param>
    private async Task ApplyEngineChangeAsync(string engineKey)
    {
        if (_registry is null) return;

        var conn = _registry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);

        // No live connection to switch on → restart path handles first-time
        // activation and the "server was never started" edge case.
        if (conn is null || !conn.IsConnected)
        {
            await ReconnectAsync();
            return;
        }

        // The free default has no in-server equivalent — Essentials selects
        // Bing/DuckDuckGo at startup when no --search-engine arg is present,
        // so the only way to land there at runtime is a reconnect that drops
        // the arg. (set_search_engine accepts the explicit engines only.)
        var targetEngine = engineKey switch
        {
            "default" => "bing",
            _ => engineKey,
        };

        if (await TrySwitchEngineViaToolAsync(conn, targetEngine))
            return;

        // Tool unavailable or call failed → fall back to the restart path so
        // the user still gets the engine they asked for, just slower.
        LoggingService.LogInfo($"[MCP] Runtime engine switch unavailable for '{engineKey}', falling back to reconnect");
        await ReconnectAsync();
    }

    /// <summary>
    /// Attempts to swap the active search engine on a running Essentials
    /// server via the <c>set_search_engine</c> MCP tool. Returns
    /// <see langword="false"/> when the tool is not registered (older
    /// Essentials) or the call fails, so the caller can fall back to a
    /// restart.
    /// </summary>
    /// <param name="conn">The live Essentials server connection.</param>
    /// <param name="engine">
    /// Concrete engine name accepted by <c>set_search_engine</c>
    /// (e.g. <c>"bing"</c>, <c>"duckduckgo"</c>, <c>"serper"</c>).
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the tool call succeeded;
    /// <see langword="false"/> otherwise.
    /// </returns>
    private static async Task<bool> TrySwitchEngineViaToolAsync(
        McpServerConnection conn, string engine)
    {
        if (!conn.Tools.Any(t => t.Name == "set_search_engine"))
            return false;

        try
        {
            using var argsDoc = JsonDocument.Parse($"{{\"engine\":\"{engine}\"}}");
            var result = await conn.CallToolWithProgressAsync(
                "set_search_engine",
                argsDoc.RootElement,
                progress: null,
                ct: CancellationToken.None);
            LoggingService.LogInfo($"[MCP] Essentials engine switched in-place to '{engine}' → {result}");
            return true;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[MCP] set_search_engine call failed for '{engine}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Rebuilds the Essentials MCP server configuration with the updated search engine and reconnects.
    /// </summary>
    private async Task ReconnectAsync()
    {
        if (_registry is null) return;

        var conn = _registry.GetBuiltInConnection(BuiltInServerHelper.EssentialsKey);
        if (conn is null) return;

        try
        {
            BuiltInServerHelper.TryRebuildBuiltInConfig(conn.Config);

            var engine = AppSettings.BuiltInServers.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config)
                ? config?.SearchEngine ?? "default"
                : "default";
            var envKeysStr = conn.Config.EnvironmentVariables is { Count: > 0 } ev
                ? string.Join(",", ev.Keys) : "";
            LoggingService.LogInfo($"[MCP] Reconnecting Essentials with search engine: {engine}, args: [{string.Join(", ", conn.Config.Arguments ?? [])}], envKeys=[{envKeysStr}]");
            await _registry.ReconnectAsync(conn);
            LoggingService.LogInfo($"[MCP] Essentials reconnected successfully (engine: {engine})");
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[MCP] Essentials reconnect failed: {ex.Message}");
        }
    }

    #endregion
}
