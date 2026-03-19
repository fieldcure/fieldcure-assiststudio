using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.Storage;

namespace AssistStudio.Helpers;

/// <summary>
/// Centralized application settings backed by <see cref="ApplicationData.Current.LocalSettings"/>.
/// Manages theme, system prompt, provider presets, profiles, MRU file paths, and model caches.
/// </summary>
public static class AppSettings
{
    #region Events

    /// <summary>
    /// Raised when the application theme changes.
    /// </summary>
    public static event EventHandler<string>? ThemeChanged;

    /// <summary>
    /// Raised when the system prompt text changes.
    /// </summary>
    public static event EventHandler<string>? SystemPromptChanged;

    /// <summary>
    /// Raised when provider presets are saved.
    /// </summary>
    public static event EventHandler? PresetsChanged;

    /// <summary>
    /// Raised when profiles are added, removed, or modified.
    /// </summary>
    public static event EventHandler? ProfilesChanged;

    /// <summary>
    /// Notifies subscribers that profiles have changed.
    /// </summary>
    public static void NotifyProfilesChanged() => ProfilesChanged?.Invoke(null, EventArgs.Empty);

    #endregion

    #region Properties

    /// <summary>
    /// Gets the underlying local settings container.
    /// </summary>
    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

    /// <summary>
    /// Gets or sets the application theme name ("Light", "Dark", or "System").
    /// </summary>
    public static string Theme
    {
        get => Settings.Values["Theme"] as string ?? "System";
        set
        {
            Settings.Values["Theme"] = value;
            ThemeChanged?.Invoke(null, value);
        }
    }

    /// <summary>
    /// Gets or sets the system prompt text used for new conversations.
    /// </summary>
    public static string SystemPrompt
    {
        get => Settings.Values["SystemPrompt"] as string ?? "";
        set
        {
            Settings.Values["SystemPrompt"] = value;
            SystemPromptChanged?.Invoke(null, value);
        }
    }

    /// <summary>
    /// Gets or sets the app tasks model source mode ("Current" or "Specific").
    /// </summary>
    public static string AppTasksSource
    {
        get => Settings.Values["AppTasksSource"] as string ?? "Current";
        set => Settings.Values["AppTasksSource"] = value;
    }

    /// <summary>
    /// Gets or sets the name of the preset used for app tasks when source is "Specific".
    /// </summary>
    public static string AppTasksPreset
    {
        get => Settings.Values["AppTasksPreset"] as string ?? "";
        set => Settings.Values["AppTasksPreset"] = value;
    }

    /// <summary>
    /// Gets or sets whether automatic conversation title generation is enabled.
    /// </summary>
    public static bool AppAutoTitle
    {
        get => Settings.Values["AppAutoTitle"] is not false;
        set => Settings.Values["AppAutoTitle"] = value;
    }

    /// <summary>
    /// Gets or sets whether automatic conversation summarization is enabled.
    /// </summary>
    public static bool AppAutoSummary
    {
        get => Settings.Values["AppAutoSummary"] is not false;
        set => Settings.Values["AppAutoSummary"] = value;
    }

    /// <summary>
    /// Gets or sets the name of the active profile (empty string means custom/manual).
    /// </summary>
    public static string ActiveProfile
    {
        get => Settings.Values["ActiveProfile"] as string ?? "Professional";
        set => Settings.Values["ActiveProfile"] = value;
    }

    /// <summary>
    /// Gets or sets the list of most recently used conversation file paths.
    /// </summary>
    public static List<string> RecentFilePaths
    {
        get
        {
            var json = Settings.Values["RecentFilePaths"] as string;
            if (string.IsNullOrEmpty(json)) return [];
            try { return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListString) ?? []; }
            catch { return []; }
        }
        set
        {
            var json = JsonSerializer.Serialize(value, AppJsonContext.Default.ListString);
            Settings.Values["RecentFilePaths"] = json;
        }
    }

    #endregion

    #region Constants

    /// <summary>
    /// Maximum number of recent files to retain in the MRU list.
    /// </summary>
    private const int MaxRecentFiles = 10;

    #endregion

    #region Fields

    /// <summary>
    /// The built-in profiles that ship with the application.
    /// </summary>
    private static readonly List<Profile> BuiltInProfiles =
    [
        new() { Name = "Professional", IsBuiltIn = true,
            Text = "You are a helpful assistant. Provide clear, well-structured responses. " +
                   "Explain step by step when needed. If you're unsure about something, say so honestly." },
        new() { Name = "Analytical", IsBuiltIn = true,
            Text = "You are a concise and direct assistant. Prioritize code readability and performance. " +
                   "Use Markdown formatting. Keep explanations brief and to the point." },
        new() { Name = "Creative", IsBuiltIn = true,
            Text = "You are a creative assistant who explores multiple perspectives. " +
                   "Suggest innovative ideas and ask follow-up questions to better understand the user's needs." },
        new() { Name = "Task Planner", IsBuiltIn = true,
            ToolNames = ["search_files", "read_file", "write_file", "run_command"],
            Text = "You are a task planner that breaks down complex requests into steps and executes them using available tools. " +
                   "Workflow: (1) Analyze the request and present a numbered step-by-step plan, " +
                   "(2) Wait for the user to approve before proceeding, " +
                   "(3) Execute each step using tools, reporting progress after each, " +
                   "(4) Summarize the final result. " +
                   "If a step fails, explain what went wrong and suggest alternatives. " +
                   "Always prefer the simplest approach. Do not over-engineer." }
    ];

    /// <summary>
    /// Duration after which cached model lists are considered stale.
    /// </summary>
    private static readonly TimeSpan ModelCacheExpiry = TimeSpan.FromHours(24);

    /// <summary>
    /// Known cloud provider definitions with their default configuration.
    /// </summary>
    private static readonly (string ProviderType, string DisplayName, string? BaseUrl, string FallbackModel)[] KnownProviders =
    [
        ("Claude", "Anthropic Claude", null, "claude-sonnet-4-20250514"),
        ("OpenAI", "OpenAI", null, "gpt-4o"),
        ("Gemini", "Google Gemini", null, "gemini-2.0-flash"),
        ("Groq", "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
    ];

    #endregion

    #region Default Model Methods

    /// <summary>
    /// Gets the saved default model ID for the specified provider.
    /// </summary>
    /// <returns>The model ID, or <c>null</c> if none is saved.</returns>
    public static string? GetDefaultModel(string provider)
    {
        return Settings.Values[$"DefaultModel_{provider}"] as string;
    }

    /// <summary>
    /// Sets the default model ID for the specified provider.
    /// </summary>
    public static void SetDefaultModel(string provider, string modelId)
    {
        Settings.Values[$"DefaultModel_{provider}"] = modelId;
    }

    /// <summary>
    /// Gets the saved Ollama server base URL, or <c>null</c> for the default (localhost).
    /// </summary>
    public static string? GetOllamaBaseUrl()
    {
        return Settings.Values["OllamaBaseUrl"] as string;
    }

    /// <summary>
    /// Sets the Ollama server base URL. Pass <c>null</c> to revert to default.
    /// </summary>
    public static void SetOllamaBaseUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            Settings.Values.Remove("OllamaBaseUrl");
        else
            Settings.Values["OllamaBaseUrl"] = url;
    }

    #endregion

    #region Profile Methods

    /// <summary>
    /// Loads all profiles, combining built-in profiles with user-created custom profiles from storage.
    /// </summary>
    /// <returns>A list of all available profiles.</returns>
    public static List<Profile> LoadProfiles()
    {
        // Clone built-in profiles so modifications don't affect the static defaults
        var result = BuiltInProfiles.Select(p => new Profile
        {
            Name = p.Name,
            Text = p.Text,
            IsBuiltIn = p.IsBuiltIn,
            PreferredProviderType = p.PreferredProviderType,
            PreferredModelId = p.PreferredModelId,
            ToolNames = [.. p.ToolNames],
            UseSearchTools = p.UseSearchTools,
        }).ToList();

        // Apply saved overrides for built-in profiles (tool settings etc.)
        var overridesJson = Settings.Values["BuiltInProfileOverrides"] as string;
        if (!string.IsNullOrEmpty(overridesJson))
        {
            try
            {
                var overrides = JsonSerializer.Deserialize(overridesJson, AppJsonContext.Default.ListProfile) ?? [];
                foreach (var ov in overrides)
                {
                    var target = result.FirstOrDefault(p => p.Name == ov.Name);
                    if (target is not null)
                    {
                        target.ToolNames = ov.ToolNames;
                        target.UseSearchTools = ov.UseSearchTools;
                    }
                }
            }
            catch { /* ignore corrupt data */ }
        }

        var json = Settings.Values["CustomProfiles"] as string;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var custom = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListProfile) ?? [];
                result.AddRange(custom);
            }
            catch { /* ignore corrupt data */ }
        }

        return result;
    }

    /// <summary>
    /// Saves profiles to local storage. Custom profiles are saved fully;
    /// built-in profiles save only tool-related overrides.
    /// </summary>
    public static void SaveCustomProfiles(IEnumerable<Profile> allProfiles)
    {
        var all = allProfiles.ToList();

        var custom = all.Where(p => !p.IsBuiltIn).ToList();
        var json = JsonSerializer.Serialize(custom, AppJsonContext.Default.ListProfile);
        Settings.Values["CustomProfiles"] = json;

        // Save tool overrides for built-in profiles
        var builtInOverrides = all
            .Where(p => p.IsBuiltIn)
            .Select(p => new Profile { Name = p.Name, ToolNames = p.ToolNames, UseSearchTools = p.UseSearchTools })
            .ToList();
        var ovJson = JsonSerializer.Serialize(builtInOverrides, AppJsonContext.Default.ListProfile);
        Settings.Values["BuiltInProfileOverrides"] = ovJson;
    }

    #endregion

    #region MRU Methods

    /// <summary>
    /// Adds a file path to the top of the most recently used list, removing duplicates and
    /// trimming to <see cref="MaxRecentFiles"/>.
    /// </summary>
    public static void AddRecentFile(string filePath)
    {
        LoggingService.LogInfo($"[File] Recent file added: {Path.GetFileName(filePath)}");
        var list = RecentFilePaths;
        list.Remove(filePath);
        list.Insert(0, filePath);
        if (list.Count > MaxRecentFiles)
            list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles);
        RecentFilePaths = list;
    }

    #endregion

    #region Model Cache Methods

    /// <summary>
    /// Stores a cached list of model IDs for the specified provider with a timestamp.
    /// </summary>
    public static void SetCachedModels(string provider, List<string> modelIds)
    {
        var json = JsonSerializer.Serialize(modelIds, AppJsonContext.Default.ListString);
        Settings.Values[$"CachedModels_{provider}"] = json;
        Settings.Values[$"CachedModels_{provider}_At"] = DateTimeOffset.UtcNow.Ticks;
    }

    /// <summary>
    /// Retrieves the cached model list for the specified provider if it has not expired.
    /// </summary>
    /// <returns>The cached model IDs, or <c>null</c> if the cache is missing or expired.</returns>
    public static List<string>? GetCachedModels(string provider)
    {
        var json = Settings.Values[$"CachedModels_{provider}"] as string;
        var ticks = Settings.Values[$"CachedModels_{provider}_At"];

        if (string.IsNullOrEmpty(json) || ticks is not long cachedTicks)
            return null;

        var cachedAt = new DateTimeOffset(cachedTicks, TimeSpan.Zero);
        if (DateTimeOffset.UtcNow - cachedAt > ModelCacheExpiry)
            return null;

        try
        {
            return JsonSerializer.Deserialize(json, AppJsonContext.Default.ListString);
        }
        catch
        {
            return null;
        }
    }

    #endregion

    #region Provider Preset Methods

    /// <summary>
    /// Persists provider presets to local storage, saving API keys securely via the password vault.
    /// </summary>
    public static void SavePresets(IList<ProviderPreset> presets)
    {
        // Save API keys to PasswordVault, serialize rest to JSON
        foreach (var p in presets)
        {
            if (p.RequiresApiKey && !string.IsNullOrEmpty(p.ApiKey))
            {
                PasswordVaultHelper.SaveApiKey(p.ProviderType, p.ApiKey);
            }
        }

        var list = presets as List<ProviderPreset> ?? [.. presets];
        var json = JsonSerializer.Serialize(list, AppJsonContext.Default.ListProviderPreset);
        Settings.Values["ProviderPresets"] = json;

        PresetsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Loads provider presets from local storage, restoring API keys from the password vault.
    /// Falls back to building presets from vault keys if no saved presets exist.
    /// </summary>
    /// <returns>An observable collection of provider presets.</returns>
    public static ObservableCollection<ProviderPreset> LoadPresets()
    {
        var json = Settings.Values["ProviderPresets"] as string;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var list = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListProviderPreset);
                if (list is { Count: > 0 })
                {
                    foreach (var p in list)
                    {
                        if (p.RequiresApiKey)
                        {
                            p.ApiKey = PasswordVaultHelper.LoadApiKey(p.ProviderType);
                        }
                    }

                    return new ObservableCollection<ProviderPreset>(list);
                }
            }
            catch { /* fall through to rebuild */ }
        }

        // No saved presets — build from PasswordVault API keys
        return BuildPresetsFromVault();
    }

    #endregion

    #region MCP Server Methods

    /// <summary>
    /// Raised when MCP server configurations change.
    /// </summary>
    public static event EventHandler? McpServersChanged;

    /// <summary>
    /// Gets the path to the MCP servers configuration file.
    /// Uses a file instead of LocalSettings to avoid the 8KB-per-value limit.
    /// </summary>
    private static string McpServersFilePath
        => Path.Combine(ApplicationData.Current.LocalFolder.Path, "mcp_servers.json");

    /// <summary>
    /// Loads MCP server configurations from the JSON file,
    /// restoring environment variable values from PasswordVault.
    /// </summary>
    public static async Task<List<McpServerConfig>> LoadMcpServersAsync()
    {
        try
        {
            if (!File.Exists(McpServersFilePath))
                return [];

            var json = await File.ReadAllTextAsync(McpServersFilePath);
            var configs = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListMcpServerConfig) ?? [];

            // Restore env var values from PasswordVault
            foreach (var config in configs)
            {
                if (config.EnvironmentVariableKeys is { Count: > 0 } keys)
                {
                    config.EnvironmentVariables = [];
                    foreach (var key in keys)
                    {
                        var value = PasswordVaultHelper.LoadMcpEnvVar(config.Id, key);
                        if (!string.IsNullOrEmpty(value))
                            config.EnvironmentVariables[key] = value;
                    }
                }
            }

            return configs;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Saves MCP server configurations to the JSON file,
    /// storing environment variable values in PasswordVault.
    /// </summary>
    public static async Task SaveMcpServersAsync(List<McpServerConfig> configs)
    {
        // Save env var values to PasswordVault, update key lists
        foreach (var config in configs)
        {
            if (config.EnvironmentVariables is { Count: > 0 } envVars)
            {
                config.EnvironmentVariableKeys = [.. envVars.Keys];
                foreach (var (key, value) in envVars)
                    PasswordVaultHelper.SaveMcpEnvVar(config.Id, key, value);
            }
            else
            {
                config.EnvironmentVariableKeys = null;
            }
        }

        var json = JsonSerializer.Serialize(configs, AppJsonContext.Default.ListMcpServerConfig);
        await File.WriteAllTextAsync(McpServersFilePath, json);

        McpServersChanged?.Invoke(null, EventArgs.Empty);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Builds a default set of provider presets by scanning the password vault for stored API keys.
    /// Always includes Ollama and Mock providers.
    /// </summary>
    /// <returns>An observable collection of provider presets constructed from vault data.</returns>
    private static ObservableCollection<ProviderPreset> BuildPresetsFromVault()
    {
        var presets = new ObservableCollection<ProviderPreset>();

        foreach (var (providerType, displayName, baseUrl, fallbackModel) in KnownProviders)
        {
            var key = PasswordVaultHelper.LoadApiKey(providerType);
            if (string.IsNullOrEmpty(key)) continue;

            var savedModel = GetDefaultModel(providerType);

            presets.Add(new ProviderPreset
            {
                Name = displayName,
                ProviderType = providerType,
                ModelId = savedModel ?? fallbackModel,
                ApiKey = key,
                BaseUrl = baseUrl,
            });
        }

        // Ollama (always available, no key needed)
        var ollamaModel = GetDefaultModel("Ollama");
        presets.Add(new ProviderPreset
        {
            Name = "Ollama",
            ProviderType = "Ollama",
            ModelId = ollamaModel ?? "llama3.1",
            BaseUrl = GetOllamaBaseUrl(),
        });

        // Mock
        presets.Add(new ProviderPreset { Name = "Mock", ProviderType = "Mock" });

        return presets;
    }

    #endregion
}
