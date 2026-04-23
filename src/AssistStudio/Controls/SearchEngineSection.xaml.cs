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
/// Self-contained section that lets the user pick the Essentials MCP search engine
/// and manage the API keys for each paid provider. Two responsibilities are kept
/// visually and behaviourally separate:
/// <list type="bullet">
///   <item>An <b>active engine</b> ComboBox picks which engine Essentials routes tool calls through.</item>
///   <item>An <b>API keys</b> area stores the provider credentials that make each paid engine eligible.</item>
/// </list>
/// Entering a key only makes that engine <i>selectable</i>; it never changes the active engine.
/// The only automatic selection change happens when the active engine loses its key, in which
/// case the selector falls back to the free Bing/DuckDuckGo engine.
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

    /// <summary>
    /// Maps engine key (e.g. "default", "serper") to the corresponding ComboBoxItem,
    /// so the paid-engine key handlers can flip IsEnabled and drive <see cref="EngineCombo"/>
    /// selection without a linear search.
    /// </summary>
    private readonly Dictionary<string, ComboBoxItem> _engineItems = new(StringComparer.Ordinal);

    private McpServerRegistry? _registry;
    private bool _loaded;

    /// <summary>
    /// Guards <see cref="OnEngineComboSelectionChanged"/> while the UI is being
    /// programmatically synced — either to the live server state via
    /// <c>get_search_engine</c>, or to the AppSettings-derived default during
    /// <see cref="LoadState"/>. The handler still fires, but must not persist
    /// to AppSettings or re-invoke the server.
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
    /// of the selector to the server's live engine.
    /// </summary>
    private void OnSectionExpanded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        LoadState();
        _ = SyncFromServerAsync();
    }

    /// <summary>
    /// Handles the engine ComboBox selection changing by persisting the choice and
    /// applying it to the running Essentials server.
    /// </summary>
    private void OnEngineComboSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingFromServer) return;
        if (EngineCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string engineKey)
            return;

        PersistAndApplyEngineChange(engineKey);
    }

    /// <summary>
    /// Handles a user keystroke on a paid-engine API key field by persisting to the
    /// credential vault and reflecting the availability change in the ComboBox item's
    /// enabled state and the row's trash-button visibility.
    /// <para>
    /// Entering a key <b>does not</b> switch the active engine — the user must still
    /// pick the engine from the ComboBox. The only automatic switch is the reverse
    /// direction: clearing the active engine's key falls back to the free default.
    /// </para>
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
        if (string.Equals(value, vm.ApiKey, StringComparison.Ordinal))
            return;

        var hasKey = !string.IsNullOrEmpty(value);

        if (hasKey)
        {
            PasswordVaultHelper.SaveMcpEnvVar(EssentialsServerId, vm.EnvKey, value);
            vm.ApiKey = value;
            vm.ClearVisibility = Visibility.Visible;
            SetEngineEnabled(vm.EngineKey, true);
            LoggingService.LogInfo($"[MCP] API key saved for {vm.EngineKey}");
        }
        else
        {
            ClearKeyAndFallbackIfActive(vm);
        }
    }

    /// <summary>
    /// Handles the per-row trash button by clearing the stored API key, disabling
    /// the corresponding ComboBox item, and falling back to the free engine if the
    /// cleared row was the active engine.
    /// </summary>
    private void OnClearPaidEngine(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PaidEngineRowViewModel vm)
            return;

        ClearKeyAndFallbackIfActive(vm);
    }

    /// <summary>
    /// Persists the Wolfram|Alpha AppID to PasswordVault and reconnects Essentials so the
    /// new <c>WOLFRAM_APPID</c> env var takes effect.
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
    /// Handles the Wolfram|Alpha trash button by emptying the PasswordBox, which
    /// causes <see cref="OnWolframKeyChanged"/> to delete the stored AppID and reconnect.
    /// </summary>
    private void OnWolframClear(object sender, RoutedEventArgs e)
    {
        WolframKey.Password = "";
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Localizes labels, validates the persisted engine against available credentials,
    /// populates the engine ComboBox (Free + three paid items with per-item enabled
    /// state), seeds the three paid-engine view models, and sets up the Wolfram|Alpha
    /// row. Called once on first expansion.
    /// </summary>
    private void LoadState()
    {
        // Localized labels + hints
        var runtimeHintText = _loader.GetString("Connect_SearchEngineRuntimeHint");
        if (!string.IsNullOrEmpty(runtimeHintText))
        {
            RuntimeHintText.Text = runtimeHintText;
            RuntimeHintText.Visibility = Visibility.Visible;
        }

        ActiveEngineLabel.Text = _loader.GetString("Connect_ActiveEngine") ?? "Active engine";
        ApiKeysHeader.Text = _loader.GetString("Connect_ApiKeysHeader") ?? "API keys";

        WolframLabel.Text = _loader.GetString("Connect_WolframAlpha");
        WolframKey.PlaceholderText = _loader.GetString("Connect_WolframAppIdPlaceholder") ?? "AppID";
        WolframHint.Text = _loader.GetString("Connect_WolframAppIdHint");
        var removeLabel = _loader.GetString("Connect_Remove") ?? "Remove";
        WolframRemoveTooltip.Content = removeLabel;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(WolframClearButton, removeLabel);

        // Current engine from AppSettings, with fallback if its key is missing.
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

        // Paid engine key rows — use key presence to seed the ComboBox enabled state.
        var apiKeyPlaceholder = _loader.GetString("Connect_ApiKeyPlaceholder") ?? "API key";
        var removeTooltip = _loader.GetString("Connect_Remove") ?? "Remove";
        _paidEngines.Clear();
        var serper = CreatePaidEngineVm("Serper", "serper", SerperEnvKey, apiKeyPlaceholder, removeTooltip);
        var tavily = CreatePaidEngineVm("Tavily", "tavily", TavilyEnvKey, apiKeyPlaceholder, removeTooltip);
        var serpApi = CreatePaidEngineVm("SerpApi", "serpapi", SerpApiEnvKey, apiKeyPlaceholder, removeTooltip);
        _paidEngines.Add(serper);
        _paidEngines.Add(tavily);
        _paidEngines.Add(serpApi);

        // Engine ComboBox — Free always, paid items enabled only when their key exists.
        _engineItems.Clear();
        EngineCombo.Items.Clear();
        var freeDisplay = $"{_loader.GetString("Connect_SearchEngineFree")} {_loader.GetString("Connect_SearchEngineFreeHint")}".Trim();
        AddEngineItem(DefaultEngineKey, freeDisplay, isEnabled: true);
        AddEngineItem(serper.EngineKey, serper.DisplayName, !string.IsNullOrEmpty(serper.ApiKey));
        AddEngineItem(tavily.EngineKey, tavily.DisplayName, !string.IsNullOrEmpty(tavily.ApiKey));
        AddEngineItem(serpApi.EngineKey, serpApi.DisplayName, !string.IsNullOrEmpty(serpApi.ApiKey));

        // Select the current engine silently — no persist, no server round-trip.
        SelectEngineInComboSilently(currentEngine);

        // Wolfram|Alpha row — seed _wolframLoadedKey BEFORE touching the PasswordBox so
        // OnWolframKeyChanged's guard recognises the ensuing assignment as the initial echo.
        var wolframKey = PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, WolframEnvKey);
        _wolframLoadedKey = wolframKey ?? "";
        if (!string.IsNullOrEmpty(wolframKey))
        {
            WolframKey.Password = wolframKey;
            WolframClearButton.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Constructs a view model for one paid search engine row, seeding
    /// <see cref="PaidEngineRowViewModel.ApiKey"/> and the clear-button visibility
    /// from the stored credential.
    /// </summary>
    private static PaidEngineRowViewModel CreatePaidEngineVm(
        string displayName, string engineKey, string envKey,
        string apiKeyPlaceholder, string removeTooltip)
    {
        var existingKey = PasswordVaultHelper.LoadMcpEnvVar(EssentialsServerId, envKey);
        var hasKey = !string.IsNullOrEmpty(existingKey);

        return new PaidEngineRowViewModel(displayName, engineKey, envKey)
        {
            ApiKey = hasKey ? existingKey : "",
            ApiKeyPlaceholder = apiKeyPlaceholder,
            RemoveTooltip = removeTooltip,
            ClearVisibility = hasKey ? Visibility.Visible : Visibility.Collapsed,
        };
    }

    /// <summary>
    /// Appends a ComboBoxItem to <see cref="EngineCombo"/> with its engine key stored
    /// on <see cref="FrameworkElement.Tag"/>, and records it in <see cref="_engineItems"/>.
    /// </summary>
    private void AddEngineItem(string engineKey, string display, bool isEnabled)
    {
        var item = new ComboBoxItem
        {
            Content = display,
            Tag = engineKey,
            IsEnabled = isEnabled,
        };
        EngineCombo.Items.Add(item);
        _engineItems[engineKey] = item;
    }

    /// <summary>
    /// Flips the enabled state of one paid engine's ComboBox item. Used when the
    /// user enters or removes a key for that engine.
    /// </summary>
    private void SetEngineEnabled(string engineKey, bool isEnabled)
    {
        if (_engineItems.TryGetValue(engineKey, out var item))
            item.IsEnabled = isEnabled;
    }

    /// <summary>
    /// Selects the ComboBoxItem whose tag matches <paramref name="engineKey"/>
    /// without firing the persist/server-apply path. Used by initial load and
    /// the server-state sync.
    /// </summary>
    private void SelectEngineInComboSilently(string engineKey)
    {
        if (!_engineItems.TryGetValue(engineKey, out var item))
            return;

        _isSyncingFromServer = true;
        try
        {
            EngineCombo.SelectedItem = item;
        }
        finally
        {
            _isSyncingFromServer = false;
        }
    }

    /// <summary>
    /// Removes the stored API key for <paramref name="vm"/> and updates the
    /// corresponding ComboBox item to disabled. If the cleared engine was the
    /// currently active one, switches the ComboBox to the free default — which
    /// fires <see cref="OnEngineComboSelectionChanged"/> and lets the normal
    /// persist path take over (AppSettings update + server runtime swap).
    /// </summary>
    private void ClearKeyAndFallbackIfActive(PaidEngineRowViewModel vm)
    {
        var wasActive = IsActiveEngine(vm.EngineKey);

        PasswordVaultHelper.DeleteMcpEnvVar(EssentialsServerId, vm.EnvKey);
        vm.ApiKey = "";
        vm.ClearVisibility = Visibility.Collapsed;
        SetEngineEnabled(vm.EngineKey, false);
        LoggingService.LogInfo($"[MCP] API key cleared for {vm.EngineKey}");

        if (wasActive)
        {
            LoggingService.LogInfo($"[MCP] Active engine '{vm.EngineKey}' lost its key, falling back to default");
            // Non-silent selection so the normal handler persists + applies.
            if (_engineItems.TryGetValue(DefaultEngineKey, out var defaultItem))
                EngineCombo.SelectedItem = defaultItem;
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when the given engine key matches the
    /// ComboBox's current selection tag.
    /// </summary>
    private bool IsActiveEngine(string engineKey)
    {
        return EngineCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && string.Equals(tag, engineKey, StringComparison.Ordinal);
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
    /// running Essentials server. No-ops when the current persisted engine already
    /// equals <paramref name="engineKey"/> — guards against the <c>SelectionChanged</c>
    /// event that <see cref="ComboBox"/> raises on initial container realization.
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
    /// (v2.4.0+) and selects the matching ComboBox item so the UI reflects the
    /// actual engine in use. Silently no-ops when the server is not connected,
    /// the tool is unavailable (older Essentials), or the call fails — in those
    /// cases the selector stays on the AppSettings-derived default that
    /// <see cref="LoadState"/> already selected.
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
            var targetTag = ResolveComboTagForServerKey(serverKey);
            if (!_engineItems.TryGetValue(targetTag, out _))
            {
                LoggingService.LogInfo($"[MCP] get_search_engine reported '{serverKey}' but no matching combo item; leaving UI as-is");
                return;
            }

            SelectEngineInComboSilently(targetTag);
            LoggingService.LogInfo($"[MCP] Search engine UI synced to server state: {serverKey} (tag='{targetTag}')");
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[MCP] get_search_engine call failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Maps the canonical engine key returned by <c>get_search_engine</c> to
    /// the ComboBox item tag used in <see cref="LoadState"/>. When the server
    /// reports <c>bing</c> and the user has not saved a paid engine, the
    /// free-default item is selected so the Bing/DDG fallback and an explicit
    /// Bing choice look identical in the UI.
    /// </summary>
    /// <param name="serverKey">Lowercase canonical key from <c>get_search_engine</c>.</param>
    /// <returns>The ComboBox item tag to select, or the server key itself for paid engines.</returns>
    private static string ResolveComboTagForServerKey(string serverKey)
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

        LoggingService.LogInfo($"[MCP] Runtime engine switch unavailable for '{engineKey}', falling back to reconnect");
        await ReconnectAsync();
    }

    /// <summary>
    /// Attempts to swap the active search engine on a running Essentials server
    /// via the <c>set_search_engine</c> MCP tool. Returns <see langword="false"/>
    /// when the tool is not registered (older Essentials) or the call fails, so
    /// the caller can fall back to a restart.
    /// </summary>
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
/// View model for one paid search engine's key-management row. Holds the stored
/// credential plus the localized labels used by the DataTemplate. The engine's
/// active/inactive state lives on the corresponding <see cref="ComboBoxItem"/>
/// in the engine ComboBox, not on the row — entering or clearing a key only
/// flips that item's availability, it does not automatically switch the active
/// engine.
/// </summary>
public sealed partial class PaidEngineRowViewModel : ObservableObject
{
    /// <summary>Initializes immutable identity fields (display + env mapping).</summary>
    /// <param name="displayName">Row label shown to the user (e.g. "Serper").</param>
    /// <param name="engineKey">Canonical server-side key (e.g. "serper").</param>
    /// <param name="envKey">PasswordVault/env var name (e.g. "SERPER_API_KEY").</param>
    public PaidEngineRowViewModel(string displayName, string engineKey, string envKey)
    {
        DisplayName = displayName;
        EngineKey = engineKey;
        EnvKey = envKey;
    }

    /// <summary>Gets the localized row label.</summary>
    public string DisplayName { get; }

    /// <summary>Gets the canonical server-side engine key.</summary>
    public string EngineKey { get; }

    /// <summary>Gets the PasswordVault / env var name for this engine's API key.</summary>
    public string EnvKey { get; }

    /// <summary>Gets or sets the placeholder shown when the API key is empty.</summary>
    public string ApiKeyPlaceholder { get; init; } = "";

    /// <summary>Gets or sets the tooltip shown on the trash button.</summary>
    public string RemoveTooltip { get; init; } = "";

    /// <summary>Gets or sets the stored API key reflected into the bound PasswordBox.</summary>
    [ObservableProperty] private string apiKey = "";

    /// <summary>Gets or sets the trash-button visibility (visible only while a key is stored).</summary>
    [ObservableProperty] private Visibility clearVisibility = Visibility.Collapsed;
}
