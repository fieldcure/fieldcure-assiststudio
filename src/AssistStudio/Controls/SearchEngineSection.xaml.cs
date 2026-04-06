using AssistStudio.Helpers;
using AssistStudio.Mcp;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Controls;

/// <summary>
/// Self-contained section for selecting the Essentials MCP search engine and managing API keys.
/// Designed to be injected into the EssentialsCard's BottomContent slot.
/// </summary>
public sealed partial class SearchEngineSection : UserControl
{
    #region Constants

    private const string SerperVaultKey = "FieldCure:Essentials:SerperApiKey";
    private const string TavilyVaultKey = "FieldCure:Essentials:TavilyApiKey";
    private const string SerpApiVaultKey = "FieldCure:Essentials:SerpApiApiKey";

    #endregion

    #region Fields

    private readonly ResourceLoader _loader = new();
    private McpServerRegistry? _registry;
    private bool _loaded;

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
    /// Handles the section expander being opened for the first time, triggering lazy UI construction.
    /// </summary>
    private void OnSectionExpanded(object? sender, EventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        BuildUI();
    }

    /// <summary>
    /// Handles search engine radio button selection changes, persists the choice, and triggers reconnection.
    /// </summary>
    private void OnSearchEngineChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton radio || radio.Tag is not string engineKey) return;

        var configs = AppSettings.BuiltInServers;
        if (!configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config))
        {
            config = new BuiltInServerConfig { IsEnabled = true };
            configs[BuiltInServerHelper.EssentialsKey] = config;
        }

        config.SearchEngine = engineKey == "default" ? null : engineKey;
        AppSettings.BuiltInServers = configs;
        LoggingService.LogInfo($"[MCP] Search engine changed to: {engineKey}");

        _ = ReconnectAsync();
    }

    /// <summary>
    /// Persists the API key, enables/selects the radio button, and shows/hides the trash button.
    /// </summary>
    private void OnApiKeyChanged(object sender, RadioButton radio, Button clearButton, string engineKey)
    {
        if (sender is not PasswordBox pb || pb.Tag is not string vaultKey) return;

        var value = pb.Password?.Trim() ?? "";
        var hasKey = !string.IsNullOrEmpty(value);

        if (hasKey)
        {
            PasswordVaultHelper.WriteDirectCredential(vaultKey, value);
            radio.IsEnabled = true;
            radio.IsChecked = true; // auto-select on key entry
            clearButton.Visibility = Visibility.Visible;
            LoggingService.LogInfo($"[MCP] API key saved for {engineKey}, auto-selected");
        }
        else
        {
            PasswordVaultHelper.DeleteDirectCredential(vaultKey);
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
        if (pb.Tag is string vaultKey)
        {
            PasswordVaultHelper.DeleteDirectCredential(vaultKey);
            LoggingService.LogInfo($"[MCP] API key cleared: {vaultKey}");
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
            var vaultKey = currentEngine switch
            {
                "serper" => SerperVaultKey,
                "tavily" => TavilyVaultKey,
                "serpapi" => SerpApiVaultKey,
                _ => null,
            };
            if (vaultKey is null || string.IsNullOrEmpty(PasswordVaultHelper.ReadDirectCredential(vaultKey)))
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
        AddPaidEngineRow(panel, "Serper", "serper", SerperVaultKey, currentEngine!, apiKeyPlaceholder);
        AddPaidEngineRow(panel, "Tavily", "tavily", TavilyVaultKey, currentEngine!, apiKeyPlaceholder);
        AddPaidEngineRow(panel, "SerpApi", "serpapi", SerpApiVaultKey, currentEngine!, apiKeyPlaceholder);
    }

    /// <summary>
    /// Adds a paid search engine row with radio button (disabled until key entered),
    /// API key input (reveal disabled), and trash button (hidden until key exists).
    /// </summary>
    private void AddPaidEngineRow(
        StackPanel parent, string displayName, string engineKey, string vaultKey,
        string currentEngine, string apiKeyPlaceholder)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var existingKey = PasswordVaultHelper.ReadDirectCredential(vaultKey);
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
            Tag = vaultKey,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // Store references for cross-updates: Tag holds vaultKey, but we need radio + clearButton too
        passwordBox.PasswordChanged += (s, _) => OnApiKeyChanged(s, radio, clearButton, engineKey);
        Grid.SetColumn(passwordBox, 1);
        row.Children.Add(passwordBox);

        // Wire clear button to password box
        clearButton.Tag = passwordBox;

        parent.Children.Add(row);
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
            var configs = AppSettings.BuiltInServers;
            configs.TryGetValue(BuiltInServerHelper.EssentialsKey, out var config);
            var engine = config?.SearchEngine ?? "default";
            var newMcpConfig = BuiltInServerHelper.CreateMcpServerConfig(
                BuiltInServerHelper.EssentialsKey, config ?? new BuiltInServerConfig { IsEnabled = true });
            if (newMcpConfig is not null)
                conn.Config.Arguments = newMcpConfig.Arguments;

            LoggingService.LogInfo($"[MCP] Reconnecting Essentials with search engine: {engine}, args: [{string.Join(", ", conn.Config.Arguments ?? [])}]");
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
