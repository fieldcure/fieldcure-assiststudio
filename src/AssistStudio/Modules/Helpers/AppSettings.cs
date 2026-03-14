using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.Storage;

using FieldCure.AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;

namespace AssistStudio.Helpers;

/// <summary>
/// Centralized application settings backed by <see cref="ApplicationData.Current.LocalSettings"/>.
/// Manages theme, system prompt, provider presets, prompt presets, MRU file paths, and model caches.
/// </summary>
public static class AppSettings
{
    #region Properties

    /// <summary>
    /// Gets the underlying local settings container.
    /// </summary>
    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

    /// <summary>
    /// Gets or sets whether this is the first time the application has been launched.
    /// </summary>
    public static bool IsFirstRun
    {
        get => Settings.Values["IsFirstRun"] is not false;
        set => Settings.Values["IsFirstRun"] = value;
    }

    /// <summary>
    /// Gets or sets the application theme name ("Light", "Dark", or "System").
    /// </summary>
    public static string Theme
    {
        get => Settings.Values["Theme"] as string ?? "System";
        set => Settings.Values["Theme"] = value;
    }

    /// <summary>
    /// Gets or sets the system prompt text used for new conversations.
    /// </summary>
    public static string SystemPrompt
    {
        get => Settings.Values["SystemPrompt"] as string ?? "";
        set => Settings.Values["SystemPrompt"] = value;
    }

    /// <summary>
    /// Gets or sets the default provider type key (e.g., "Claude", "OpenAI", "Mock").
    /// </summary>
    public static string DefaultProvider
    {
        get => Settings.Values["DefaultProvider"] as string ?? "Mock";
        set => Settings.Values["DefaultProvider"] = value;
    }

    /// <summary>
    /// Gets or sets the utility AI source mode ("Current" or "Specific").
    /// </summary>
    public static string UtilityAISource
    {
        get => Settings.Values["UtilityAISource"] as string ?? "Current";
        set => Settings.Values["UtilityAISource"] = value;
    }

    /// <summary>
    /// Gets or sets the name of the preset used for utility AI operations when source is "Specific".
    /// </summary>
    public static string UtilityAIPreset
    {
        get => Settings.Values["UtilityAIPreset"] as string ?? "";
        set => Settings.Values["UtilityAIPreset"] = value;
    }

    /// <summary>
    /// Gets or sets whether automatic conversation title generation is enabled.
    /// </summary>
    public static bool UtilityAutoTitle
    {
        get => Settings.Values["UtilityAutoTitle"] is not false;
        set => Settings.Values["UtilityAutoTitle"] = value;
    }

    /// <summary>
    /// Gets or sets whether automatic conversation summarization is enabled.
    /// </summary>
    public static bool UtilityAutoSummary
    {
        get => Settings.Values["UtilityAutoSummary"] is not false;
        set => Settings.Values["UtilityAutoSummary"] = value;
    }

    /// <summary>
    /// Gets or sets the name of the active prompt preset (empty string means custom/manual).
    /// </summary>
    public static string ActivePromptPreset
    {
        get => Settings.Values["ActivePromptPreset"] as string ?? "Professional";
        set => Settings.Values["ActivePromptPreset"] = value;
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
            try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
            catch { return []; }
        }
        set
        {
            var json = JsonSerializer.Serialize(value);
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
    /// The built-in prompt presets that ship with the application.
    /// </summary>
    private static readonly List<PromptPreset> BuiltInPromptPresets =
    [
        new() { Name = "Professional", IsBuiltIn = true,
            Text = "You are a helpful assistant. Provide clear, well-structured responses. " +
                   "Explain step by step when needed. If you're unsure about something, say so honestly." },
        new() { Name = "Analytical", IsBuiltIn = true,
            Text = "You are a concise and direct assistant. Prioritize code readability and performance. " +
                   "Use Markdown formatting. Keep explanations brief and to the point." },
        new() { Name = "Creative", IsBuiltIn = true,
            Text = "You are a creative assistant who explores multiple perspectives. " +
                   "Suggest innovative ideas and ask follow-up questions to better understand the user's needs." }
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

    #endregion

    #region Prompt Preset Methods

    /// <summary>
    /// Loads all prompt presets, combining built-in presets with user-created custom presets from storage.
    /// </summary>
    /// <returns>A list of all available prompt presets.</returns>
    public static List<PromptPreset> LoadPromptPresets()
    {
        var result = new List<PromptPreset>(BuiltInPromptPresets);

        var json = Settings.Values["CustomPromptPresets"] as string;
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var custom = JsonSerializer.Deserialize<List<PromptPreset>>(json) ?? [];
                result.AddRange(custom);
            }
            catch { /* ignore corrupt data */ }
        }

        return result;
    }

    /// <summary>
    /// Saves only the custom (non-built-in) prompt presets to local storage.
    /// </summary>
    public static void SaveCustomPromptPresets(IEnumerable<PromptPreset> allPresets)
    {
        var custom = allPresets.Where(p => !p.IsBuiltIn).ToList();
        var json = JsonSerializer.Serialize(custom);
        Settings.Values["CustomPromptPresets"] = json;
    }

    #endregion

    #region MRU Methods

    /// <summary>
    /// Adds a file path to the top of the most recently used list, removing duplicates and
    /// trimming to <see cref="MaxRecentFiles"/>.
    /// </summary>
    public static void AddRecentFile(string filePath)
    {
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
        var json = JsonSerializer.Serialize(modelIds);
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
            return JsonSerializer.Deserialize<List<string>>(json);
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
                PasswordVaultHelper.SaveApiKey(p.Name, p.ApiKey);
            }
        }

        var json = JsonSerializer.Serialize(presets);
        Settings.Values["ProviderPresets"] = json;
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
                var list = JsonSerializer.Deserialize<List<ProviderPreset>>(json);
                if (list is { Count: > 0 })
                {
                    foreach (var p in list)
                    {
                        if (p.RequiresApiKey)
                        {
                            p.ApiKey = PasswordVaultHelper.LoadApiKey(p.Name);
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
            ModelId = ollamaModel ?? "llama3.2",
        });

        // Mock
        presets.Add(new ProviderPreset { Name = "Mock", ProviderType = "Mock" });

        return presets;
    }

    #endregion
}
