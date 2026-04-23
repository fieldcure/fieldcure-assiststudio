using AssistStudio.Helpers;
using AssistStudio.Mcp;
using CommunityToolkit.Mvvm.ComponentModel;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Collections.ObjectModel;
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

    private const string DefaultEngineKey = "default";

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private readonly ObservableCollection<PaidEngineRowViewModel> _paidEngines = [];
    private McpServerRegistry? _registry;
    private bool _loaded;

    /// <summary>
    /// Guards <see cref="OnFreeEngineChecked"/> / <see cref="OnPaidEngineChecked"/>
    /// while the UI is being programmatically synced — either to the live server state
    /// via <c>get_search_engine</c>, or during mutual-exclusion enforcement after a
    /// user click. The handlers still fire, but must not persist to AppSettings or
    /// re-invoke the server.
    /// </summary>
    private bool _isSyncingFromServer;

    /// <summary>
    /// Last value programmatically assigned to <see cref="WolframKey"/>.Password so
    /// <see cref="OnWolframKeyChanged"/> can distinguish initial-load echo from a real
    /// user edit. Seeding the PasswordBox with the stored key during <see cref="LoadState"/>
    /// fires <c>PasswordChanged</c> exactly once before the user has touched anything;
    /// without this guard that echo would trigger an Essentials reconnect on every
    /// first expand when a Wolfram AppID is already saved.
    /// </summary>
    private string _wolframLoadedKey = "";

    #endregion

    #region Constructor

    /// <summary>Initializes the control and binds the paid-engine ItemsControl to its backing collection.</summary>
    public SearchEngineSection()
    {
        InitializeComponent();
        PaidEnginesList.ItemsSource = _paidEngines;
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
    /// lazy state load and (when Essentials is already connected) a one-shot sync
    /// of the radio selection to the server's live engine. The server's state
    /// can drift from AppSettings after a mid-conversation <c>set_search_engine</c>
    /// call, and the saved value is only the next-restart default — without this
    /// sync the UI would misrepresent what search actually happens when the user
    /// triggers a tool.
    /// </summary>
    private void OnSectionExpanded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        LoadState();
        _ = SyncFromServerAsync();
    }

    /// <summary>
    /// Handles the free (Bing/DuckDuckGo) radio becoming checked by unchecking the
    /// paid engines and persisting the default selection. See
    /// <see cref="PersistAndApplyEngineChange(string)"/> for the runtime-swap vs
    /// reconnect policy.
    /// </summary>
    private void OnFreeEngineChecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingFromServer) return;

        // Mutex — RadioButton GroupName crossing an ItemsControl boundary is unreliable.
        _isSyncingFromServer = true;
        try
        {
            foreach (var other in _paidEngines)
                other.IsChecked = false;
        }
        finally
        {
            _isSyncingFromServer = false;
        }

        PersistAndApplyEngineChange(DefaultEngineKey);
    }

    /// <summary>
    /// Handles a paid-engine radio becoming checked by unchecking the free radio
    /// and the other paid rows, then persisting the selection and attempting a
    /// runtime in-place switch via <c>set_search_engine</c>.
    /// </summary>
    private void OnPaidEngineChecked(object sender, RoutedEventArgs e)
    {
        if (_isSyncingFromServer) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not PaidEngineRowViewModel vm)
            return;

        _isSyncingFromServer = true;
        try
        {
            FreeEngineRadio.IsChecked = false;
            foreach (var other in _paidEngines)
            {
                if (!ReferenceEquals(other, vm))
                    other.IsChecked = false;
            }
        }
        finally
        {
            _isSyncingFromServer = false;
        }

        PersistAndApplyEngineChange(vm.EngineKey);
    }

    /// <summary>
    /// Handles a user keystroke on a paid-engine API key field by persisting to the
    /// credential vault and reflecting the availability change in the row's enabled
    /// state, auto-check behaviour, and trash-button visibility.
    /// <para>
    /// Skips work when the new value matches the view model's stored key, which
    /// suppresses the initial <c>PasswordChanged</c> echo from the OneWay
    /// <c>Password</c> binding during container realization.
    /// </para>
    /// </summary>
    private void OnPaidEngineKeyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb || pb.DataContext is not PaidEngineRowViewModel vm)
            return;

        var value = pb.Password?.Trim() ?? "";

        // Initial-load echo: the OneWay Password binding fires PasswordChanged once
        // when the container is realized with vm.ApiKey. Treat "no delta" as no-op
        // so a saved key does not re-save and auto-select on every first expand.
        if (string.Equals(value, vm.ApiKey, StringComparison.Ordinal))
            return;

        var hasKey = !string.IsNullOrEmpty(value);

        if (hasKey)
        {
            PasswordVaultHelper.SaveMcpEnvVar(EssentialsServerId, vm.EnvKey, value);
            vm.ApiKey = value;
            vm.IsEnabled = true;
            vm.IsChecked = true; // auto-select on key entry — triggers mutex via OnPaidEngineChecked
            vm.ClearVisibility = Visibility.Visible;
            LoggingService.LogInfo($"[MCP] API key saved for {vm.EngineKey}, auto-selected");
        }
        else
        {
            // Capture UI state BEFORE mutating so the fallback decision reflects the
            // engine the user was actually using — not AppSettings, which may be stale
            // after a mid-conversation set_search_engine runtime swap.
            var wasActive = vm.IsChecked;

            PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, vm.EnvKey);
            vm.ApiKey = "";
            vm.IsEnabled = false;
            vm.IsChecked = false;
            vm.ClearVisibility = Visibility.Collapsed;
            LoggingService.LogInfo($"[MCP] API key removed for {vm.EngineKey}, radio disabled");

            if (wasActive)
            {
                LoggingService.LogInfo($"[MCP] Active engine '{vm.EngineKey}' lost API key, falling back to default");
                FreeEngineRadio.IsChecked = true;
            }
        }
    }

    /// <summary>
    /// Handles the per-row trash button by clearing the stored API key, disabling
    /// the row, and falling back to the free engine if the cleared row was active
    /// in the UI (which mirrors live server state after <see cref="SyncFromServerAsync"/>).
    /// </summary>
    private void OnClearPaidEngine(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PaidEngineRowViewModel vm)
            return;

        // Capture BEFORE mutating — see OnPaidEngineKeyChanged for the rationale.
        var wasActive = vm.IsChecked;

        PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, vm.EnvKey);
        vm.ApiKey = "";
        vm.IsEnabled = false;
        vm.IsChecked = false;
        vm.ClearVisibility = Visibility.Collapsed;
        LoggingService.LogInfo($"[MCP] API key cleared: {vm.EnvKey}");

        if (wasActive)
            FreeEngineRadio.IsChecked = true;
    }

    /// <summary>
    /// Persists the Wolfram|Alpha AppID to PasswordVault and reconnects Essentials so the
    /// new <c>WOLFRAM_APPID</c> env var takes effect.
    /// <para>
    /// Compares against <see cref="_wolframLoadedKey"/> to ignore the initial
    /// <c>PasswordChanged</c> echo when <see cref="LoadState"/> seeds the box with
    /// the existing key — otherwise every first expand would trigger a gratuitous
    /// Essentials reconnect.
    /// </para>
    /// </summary>
    private void OnWolframKeyChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox pb) return;

        var value = pb.Password?.Trim() ?? "";
        if (string.Equals(value, _wolframLoadedKey, StringComparison.Ordinal))
            return;

        _wolframLoadedKey = value;
        var hasKey = !string.IsNullOrEmpty(value);

        if (hasKey)
        {
            PasswordVaultHelper.SaveMcpEnvVar(EssentialsServerId, WolframEnvKey, value);
            WolframClearButton.Visibility = Visibility.Visible;
            LoggingService.LogInfo("[MCP] Wolfram|Alpha AppID saved");
        }
        else
        {
            PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, WolframEnvKey);
            WolframClearButton.Visibility = Visibility.Collapsed;
            LoggingService.LogInfo("[MCP] Wolfram|Alpha AppID removed");
        }

        _ = ReconnectAsync();
    }

    /// <summary>
    /// Handles the Wolfram|Alpha trash button by clearing the stored AppID and
    /// hiding the trash button. The <see cref="OnWolframKeyChanged"/> handler
    /// picks up the empty value through the subsequent PasswordChanged event
    /// and reconnects.
    /// </summary>
    private void OnWolframClear(object sender, RoutedEventArgs e)
    {
        WolframKey.Password = "";
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Localizes labels, validates the persisted engine against available credentials,
    /// and populates the free-radio state, the three paid-engine view models, and the
    /// Wolfram|Alpha row. Called once on first expansion.
    /// </summary>
    private void LoadState()
    {
        // Localized hints + labels
        var runtimeHintText = _loader.GetString("Connect_SearchEngineRuntimeHint");
        if (!string.IsNullOrEmpty(runtimeHintText))
        {
            RuntimeHintText.Text = runtimeHintText;
            RuntimeHintText.Visibility = Visibility.Visible;
        }

        FreeEngineLabel.Text = _loader.GetString("Connect_SearchEngineFree");
        FreeEngineHint.Text = _loader.GetString("Connect_SearchEngineFreeHint");

        WolframLabel.Text = _loader.GetString("Connect_WolframAlpha");
        WolframKey.PlaceholderText = _loader.GetString("Connect_WolframAppIdPlaceholder") ?? "AppID";
        WolframHint.Text = _loader.GetString("Connect_WolframAppIdHint");
        WolframRemoveTooltip.Content = _loader.GetString("Connect_Remove");

        // Resolve current engine and validate that the corresponding key is still stored.
        var configs = AppSettings.BuiltInServers;
        configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var essentialsConfig);
        var rawEngine = essentialsConfig?.SearchEngine;
        var currentEngine = string.IsNullOrEmpty(rawEngine) ? DefaultEngineKey : rawEngine;

        if (currentEngine is not DefaultEngineKey)
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
                currentEngine = DefaultEngineKey;
                if (essentialsConfig is not null)
                {
                    essentialsConfig.SearchEngine = null;
                    AppSettings.BuiltInServers = configs;
                    LoggingService.LogInfo("[MCP] Reconnecting Essentials with default search engine");
                    _ = ReconnectAsync();
                }
            }
        }

        // Free radio — set directly without firing the handler so LoadState stays silent.
        _isSyncingFromServer = true;
        try
        {
            FreeEngineRadio.IsChecked = currentEngine is DefaultEngineKey;
        }
        finally
        {
            _isSyncingFromServer = false;
        }

        // Paid engine rows
        var apiKeyPlaceholder = _loader.GetString("Connect_ApiKeyPlaceholder") ?? "API key";
        var removeTooltip = _loader.GetString("Connect_Remove") ?? "Remove";
        _paidEngines.Clear();
        _paidEngines.Add(CreatePaidEngineVm("Serper", "serper", SerperEnvKey, currentEngine, apiKeyPlaceholder, removeTooltip));
        _paidEngines.Add(CreatePaidEngineVm("Tavily", "tavily", TavilyEnvKey, currentEngine, apiKeyPlaceholder, removeTooltip));
        _paidEngines.Add(CreatePaidEngineVm("SerpApi", "serpapi", SerpApiEnvKey, currentEngine, apiKeyPlaceholder, removeTooltip));

        // Wolfram|Alpha row — seed _wolframLoadedKey BEFORE touching the PasswordBox
        // so OnWolframKeyChanged's guard recognises the ensuing Password assignment as
        // the initial echo (not a user edit) and skips the reconnect.
        var wolframKey = PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, WolframEnvKey);
        _wolframLoadedKey = wolframKey ?? "";
        if (!string.IsNullOrEmpty(wolframKey))
        {
            WolframKey.Password = wolframKey;
            WolframClearButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Constructs a view model for one paid search engine row, deriving its
    /// initial state from the stored API key and the caller's active engine.
    /// </summary>
    private static PaidEngineRowViewModel CreatePaidEngineVm(
        string displayName, string engineKey, string envKey,
        string currentEngine, string apiKeyPlaceholder, string removeTooltip)
    {
        var existingKey = PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, envKey);
        var hasKey = !string.IsNullOrEmpty(existingKey);

        return new PaidEngineRowViewModel(displayName, engineKey, envKey)
        {
            ApiKey = hasKey ? existingKey : "",
            ApiKeyPlaceholder = apiKeyPlaceholder,
            RemoveTooltip = removeTooltip,
            IsChecked = currentEngine == engineKey,
            IsEnabled = hasKey,
            ClearVisibility = hasKey ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    /// <summary>
    /// Returns the currently configured search engine key from AppSettings.
    /// </summary>
    private static string GetCurrentEngine()
    {
        var configs = AppSettings.BuiltInServers;
        configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config);
        return config?.SearchEngine ?? DefaultEngineKey;
    }

    /// <summary>
    /// Persists the engine choice to <see cref="AppSettings"/> and applies it to the
    /// running Essentials server. Since v2.3.0 Essentials exposes a <c>set_search_engine</c>
    /// MCP tool that swaps the active engine in place; the restart path is only taken when
    /// the runtime swap is unavailable (server not connected, tool call fails, or the
    /// selection is the free default which has no paid fallback considerations).
    /// <para>
    /// No-ops when the current persisted engine already equals <paramref name="engineKey"/>.
    /// The initial <c>IsChecked="{x:Bind ... TwoWay}"</c> binding on the paid-row RadioButton
    /// fires <c>Checked</c> once during container realization even without user interaction,
    /// which would otherwise re-save AppSettings and issue a redundant <c>set_search_engine</c>
    /// tool call on every first expand.
    /// </para>
    /// </summary>
    private void PersistAndApplyEngineChange(string engineKey)
    {
        if (GetCurrentEngine() == engineKey)
            return;

        var configs = AppSettings.BuiltInServers;
        if (!configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config))
        {
            config = new BuiltInServerConfig { IsEnabled = true };
            configs[BuiltInServerHelper.EssentialsKey] = config;
        }

        config.SearchEngine = engineKey == DefaultEngineKey ? null : engineKey;
        AppSettings.BuiltInServers = configs;
        LoggingService.LogInfo($"[MCP] Search engine changed to: {engineKey}");

        _ = ApplyEngineChangeAsync(engineKey);
    }

    /// <summary>
    /// Queries the live Essentials server's <c>get_search_engine</c> tool
    /// (v2.4.0+) and flips the matching radio button so the UI reflects the
    /// actual engine in use. Silently no-ops when the server is not connected,
    /// the tool is unavailable (older Essentials), or the call fails — in those
    /// cases the radio stays on the AppSettings-derived default that
    /// <see cref="LoadState"/> already selected, which is the closest
    /// approximation we have without a live read.
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

            // The "default" UI option has no server-side equivalent — Essentials always
            // reports a concrete engine key. Map bing back to "default" only when the
            // user has not explicitly saved a paid engine, so a user who chose Bing
            // and a fallback-produced Bing look the same.
            var targetTag = ResolveRadioTagForServerKey(serverKey);
            if (!CheckEngineByTag(targetTag))
                LoggingService.LogInfo($"[MCP] get_search_engine reported '{serverKey}' but no matching radio; leaving UI as-is");
            else
                LoggingService.LogInfo($"[MCP] Search engine UI synced to server state: {serverKey} (radio='{targetTag}')");
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[MCP] get_search_engine call failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps the canonical engine key returned by <c>get_search_engine</c> to
    /// the radio tag used in <see cref="LoadState"/>. When the server reports
    /// <c>bing</c> and the user has not saved a paid engine, the free-default
    /// radio is selected so the Bing/DDG fallback and an explicit Bing choice
    /// look identical in the UI.
    /// </summary>
    /// <param name="serverKey">Lowercase canonical key from <c>get_search_engine</c>.</param>
    /// <returns>The radio tag to check, or the server key itself for paid engines.</returns>
    private static string ResolveRadioTagForServerKey(string serverKey)
    {
        var saved = GetCurrentEngine();
        if (string.Equals(serverKey, "bing", StringComparison.OrdinalIgnoreCase)
            && saved == DefaultEngineKey)
        {
            return DefaultEngineKey;
        }
        return serverKey;
    }

    /// <summary>
    /// Programmatically selects the radio matching <paramref name="tag"/> without
    /// firing persistence or server-side logic. Used by the server-state sync path.
    /// </summary>
    /// <returns><see langword="true"/> when a matching radio was found and toggled;
    /// otherwise <see langword="false"/>.</returns>
    private bool CheckEngineByTag(string tag)
    {
        _isSyncingFromServer = true;
        try
        {
            if (string.Equals(tag, DefaultEngineKey, StringComparison.OrdinalIgnoreCase))
            {
                FreeEngineRadio.IsChecked = true;
                foreach (var vm in _paidEngines) vm.IsChecked = false;
                return true;
            }

            var match = _paidEngines.FirstOrDefault(v =>
                string.Equals(v.EngineKey, tag, StringComparison.OrdinalIgnoreCase));
            if (match is null) return false;

            match.IsChecked = true;
            FreeEngineRadio.IsChecked = false;
            foreach (var other in _paidEngines)
            {
                if (!ReferenceEquals(other, match))
                    other.IsChecked = false;
            }
            return true;
        }
        finally
        {
            _isSyncingFromServer = false;
        }
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

        // No live connection to switch on → restart path handles first-time activation
        // and the "server was never started" edge case.
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
            DefaultEngineKey => "bing",
            _ => engineKey,
        };

        if (await TrySwitchEngineViaToolAsync(conn, targetEngine))
            return;

        // Tool unavailable or call failed → fall back to the restart path so the user
        // still gets the engine they asked for, just slower.
        LoggingService.LogInfo($"[MCP] Runtime engine switch unavailable for '{engineKey}', falling back to reconnect");
        await ReconnectAsync();
    }

    /// <summary>
    /// Attempts to swap the active search engine on a running Essentials server
    /// via the <c>set_search_engine</c> MCP tool. Returns <see langword="false"/>
    /// when the tool is not registered (older Essentials) or the call fails, so
    /// the caller can fall back to a restart.
    /// </summary>
    /// <param name="conn">The live Essentials server connection.</param>
    /// <param name="engine">
    /// Concrete engine name accepted by <c>set_search_engine</c>
    /// (e.g. <c>"bing"</c>, <c>"duckduckgo"</c>, <c>"serper"</c>).
    /// </param>
    /// <returns><see langword="true"/> when the tool call succeeded; otherwise <see langword="false"/>.</returns>
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
                ? config?.SearchEngine ?? DefaultEngineKey
                : DefaultEngineKey;
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

/// <summary>
/// View model for one paid search engine row inside <see cref="SearchEngineSection"/>.
/// Exposes the radio/password/trash state shared by Serper, Tavily, and SerpApi.
/// </summary>
public sealed partial class PaidEngineRowViewModel : ObservableObject
{
    /// <summary>Initializes immutable identity fields (display + env mapping).</summary>
    /// <param name="displayName">Radio label shown to the user (e.g. "Serper").</param>
    /// <param name="engineKey">Canonical server-side key (e.g. "serper").</param>
    /// <param name="envKey">PasswordVault/env var name (e.g. "SERPER_API_KEY").</param>
    public PaidEngineRowViewModel(string displayName, string engineKey, string envKey)
    {
        DisplayName = displayName;
        EngineKey = engineKey;
        EnvKey = envKey;
    }

    /// <summary>Gets the localized radio label.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the canonical server-side engine key.</summary>
    public string EngineKey { get; }

    /// <summary>Gets the PasswordVault / env var name for this engine's API key.</summary>
    public string EnvKey { get; }

    /// <summary>Gets or sets the placeholder shown when the API key is empty.</summary>
    public string ApiKeyPlaceholder { get; init; } = "";

    /// <summary>Gets or sets the tooltip shown on the trash button.</summary>
    public string RemoveTooltip { get; init; } = "";

    /// <summary>Gets or sets whether this row's radio is currently selected.</summary>
    [ObservableProperty] private bool isChecked;

    /// <summary>Gets or sets whether the radio is enabled (a stored key makes it selectable).</summary>
    [ObservableProperty] private bool isEnabled;

    /// <summary>Gets or sets the stored API key reflected into the bound PasswordBox.</summary>
    [ObservableProperty] private string apiKey = "";

    /// <summary>Gets or sets the trash-button visibility (visible only while a key is stored).</summary>
    [ObservableProperty] private Visibility clearVisibility = Visibility.Collapsed;
}
