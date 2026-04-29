using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Models;
using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace AssistStudio.Helpers;

/// <summary>
/// Centralized application settings backed by <see cref="ApplicationData.Current.LocalSettings"/>.
/// Manages theme, system prompt, provider models, profiles, MRU file paths, and model caches.
/// </summary>
public static class AppSettings
{
    #region Events

    /// <summary>
    /// Raised when the application theme changes.
    /// </summary>
    public static event EventHandler<string>? ThemeChanged;

    /// <summary>
    /// Raised when app task settings change (auto-title, auto-summary, max input tokens).
    /// </summary>
    public static event EventHandler? TaskSettingsChanged;

    /// <summary>
    /// Raised when provider models are saved.
    /// </summary>
    public static event EventHandler? ModelsChanged;

    /// <summary>
    /// Raised when profiles are added, removed, or modified (list-level changes).
    /// </summary>
    public static event EventHandler? ProfilesChanged;

    /// <summary>
    /// Notifies subscribers that profiles have changed.
    /// </summary>
    public static void NotifyProfilesChanged() => ProfilesChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>Shared in-memory profile cache. All callers get the same instances.</summary>
    private static List<Profile>? _profileCache;

    #endregion

    #region Properties

    /// <summary>
    /// Gets the underlying local settings container.
    /// </summary>
    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

    /// <summary>
    /// Gets or sets the application theme name ("Light", "Dark", or "System").
    /// Setting this property persists the value and raises <see cref="ThemeChanged"/>.
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
    /// Persists the theme value without raising <see cref="ThemeChanged"/>.
    /// Used by <see cref="ThemeSettingsService"/> which manages its own event.
    /// </summary>
    internal static void SetThemeSilent(string theme)
    {
        Settings.Values["Theme"] = theme;
    }

    /// <summary>
    /// Gets or sets the system prompt text used for new conversations.
    /// </summary>
    public static string SystemPrompt
    {
        get => Settings.Values["SystemPrompt"] as string ?? "";
        set => Settings.Values["SystemPrompt"] = value;
    }

    #region Per-Task Provider Settings

    /// <summary>
    /// Gets or sets the title generation provider source ("Inherit" or "Specific").
    /// </summary>
    public static string TitleSource
    {
        get => Settings.Values["TitleSource"] as string ?? "Inherit";
        set { Settings.Values["TitleSource"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the model name for title generation when <see cref="TitleSource"/> is "Specific".
    /// </summary>
    public static string TitleModel
    {
        get => Settings.Values["TitleModel"] as string ?? "";
        set { Settings.Values["TitleModel"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the summary provider source ("Inherit" or "Specific").
    /// </summary>
    public static string SummarySource
    {
        get => Settings.Values["SummarySource"] as string ?? "Inherit";
        set { Settings.Values["SummarySource"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the model name for summary when <see cref="SummarySource"/> is "Specific".
    /// </summary>
    public static string SummaryModel
    {
        get => Settings.Values["SummaryModel"] as string ?? "";
        set { Settings.Values["SummaryModel"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the sub-agent provider source ("Inherit" or "Specific").
    /// </summary>
    public static string SubAgentSource
    {
        get => Settings.Values["SubAgentSource"] as string ?? "Inherit";
        set { Settings.Values["SubAgentSource"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the model name for sub-agent when <see cref="SubAgentSource"/> is "Specific".
    /// </summary>
    public static string SubAgentModel
    {
        get => Settings.Values["SubAgentModel"] as string ?? "";
        set { Settings.Values["SubAgentModel"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// One-time migration: moves legacy <c>AppTasksSource</c>/<c>AppTasksPreset</c>
    /// to <see cref="TitleSource"/>/<see cref="TitleModel"/> and deletes the old keys.
    /// Call once at app startup.
    /// </summary>
    public static void MigrateAppTasksSettings()
    {
        if (Settings.Values.ContainsKey("AppTasksSource"))
        {
            var source = Settings.Values["AppTasksSource"] as string;
            var preset = Settings.Values["AppTasksPreset"] as string;

            if (source == "Specific" && !string.IsNullOrEmpty(preset))
            {
                // Only migrate if user had explicitly set a specific model
                Settings.Values["TitleSource"] = "Specific";
                Settings.Values["TitleModel"] = preset;
            }

            Settings.Values.Remove("AppTasksSource");
            Settings.Values.Remove("AppTasksPreset");
        }
    }

    /// <summary>
    /// One-time migration of auxiliary "*Preset" persisted keys to "*Model" keys.
    /// Idempotent. Called once at app startup before any consumer reads them.
    /// </summary>
    internal static void MigrateAuxiliaryModelKeys()
    {
        var settings = Settings.Values;
        (string Old, string New)[] pairs =
        {
            ("TitlePreset",          "TitleModel"),
            ("SummaryPreset",        "SummaryModel"),
            ("SubAgentPreset",       "SubAgentModel"),
            ("EmbeddingPreset",      "EmbeddingProviderModel"),
            ("ContextualizerPreset", "ContextualizerProviderModel"),
        };
        foreach (var (oldKey, newKey) in pairs)
        {
            if (settings.ContainsKey(newKey)) { settings.Remove(oldKey); continue; }
            if (settings.TryGetValue(oldKey, out var raw) && raw is string val)
            {
                settings[newKey] = val;
                settings.Remove(oldKey);
            }
        }

        // Specialist keys: real format is "Specialist_{name}_Preset" → rename
        // suffix to "_Model". Enumerated by suffix because the middle segment
        // is user-defined.
        var specialistKeys = settings.Keys
            .Where(k => k.StartsWith("Specialist_", StringComparison.Ordinal)
                     && k.EndsWith("_Preset", StringComparison.Ordinal))
            .ToList();
        foreach (var oldKey in specialistKeys)
        {
            var newKey = oldKey[..^"_Preset".Length] + "_Model";
            if (!settings.ContainsKey(newKey) && settings[oldKey] is string val)
                settings[newKey] = val;
            settings.Remove(oldKey);
        }
    }

    #endregion

    /// <summary>
    /// Gets or sets whether automatic conversation title generation is enabled.
    /// </summary>
    public static bool AppAutoTitle
    {
        get => Settings.Values["AppAutoTitle"] is not false;
        set { Settings.Values["AppAutoTitle"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets whether automatic conversation summarization is enabled.
    /// </summary>
    public static bool AppAutoSummary
    {
        get => Settings.Values["AppAutoSummary"] is true;
        set { Settings.Values["AppAutoSummary"] = value; TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the input token threshold for automatic summarization.
    /// When input tokens exceed this value, the conversation is summarized.
    /// Default is 100000. Set to 0 to disable threshold-based triggering.
    /// </summary>
    public static int AppMaxInputTokens
    {
        get => Settings.Values["AppMaxInputTokens"] is int v ? v : 100000;
        set { Settings.Values["AppMaxInputTokens"] = Math.Max(1000, value); TaskSettingsChanged?.Invoke(null, EventArgs.Empty); }
    }

    /// <summary>
    /// Gets or sets the ProviderModel name used for embedding (UI reference only).
    /// </summary>
    [Obsolete("Embedding config moved to per-KB config.json. Will be removed in a future version.")]
    public static string EmbeddingProviderModel
    {
        get => Settings.Values["EmbeddingProviderModel"] as string ?? "";
        set => Settings.Values["EmbeddingProviderModel"] = value;
    }

    /// <summary>
    /// Gets or sets the embedding model identifier (e.g., "nomic-embed-text").
    /// </summary>
    [Obsolete("Embedding config moved to per-KB config.json. Will be removed in a future version.")]
    public static string EmbeddingModel
    {
        get => Settings.Values["EmbeddingModel"] as string ?? "";
        set => Settings.Values["EmbeddingModel"] = value;
    }

    /// <summary>
    /// Gets or sets the embedding API base URL.
    /// Stored independently of the ProviderModel so it survives ProviderModel deletion.
    /// </summary>
    [Obsolete("Embedding config moved to per-KB config.json. Will be removed in a future version.")]
    public static string EmbeddingBaseUrl
    {
        get => Settings.Values["EmbeddingBaseUrl"] as string ?? "";
        set => Settings.Values["EmbeddingBaseUrl"] = value;
    }

    /// <summary>
    /// Gets or sets the contextualizer ProviderModel tag for UI restoration (e.g., "ollama/gemma3:4b", "none").
    /// </summary>
    [Obsolete("Contextualizer config moved to per-KB config.json. Will be removed in a future version.")]
    public static string ContextualizerProviderModel
    {
        get => Settings.Values["ContextualizerProviderModel"] as string ?? "";
        set => Settings.Values["ContextualizerProviderModel"] = value;
    }

    /// <summary>
    /// Gets or sets the contextualizer model identifier (e.g., "gemma3:4b", "gpt-4o-mini").
    /// </summary>
    [Obsolete("Contextualizer config moved to per-KB config.json. Will be removed in a future version.")]
    public static string ContextualizerModel
    {
        get => Settings.Values["ContextualizerModel"] as string ?? "";
        set => Settings.Values["ContextualizerModel"] = value;
    }

    /// <summary>
    /// Gets or sets the contextualizer API base URL.
    /// </summary>
    [Obsolete("Contextualizer config moved to per-KB config.json. Will be removed in a future version.")]
    public static string ContextualizerBaseUrl
    {
        get => Settings.Values["ContextualizerBaseUrl"] as string ?? "";
        set => Settings.Values["ContextualizerBaseUrl"] = value;
    }

    /// <summary>
    /// Gets or sets the contextualizer provider type ("openai" for Ollama/OpenAI, "anthropic" for Claude, "" for disabled).
    /// </summary>
    [Obsolete("Contextualizer config moved to per-KB config.json. Will be removed in a future version.")]
    public static string ContextualizerProvider
    {
        get => Settings.Values["ContextualizerProvider"] as string ?? "";
        set => Settings.Values["ContextualizerProvider"] = value;
    }

    /// <summary>
    /// Gets or sets the name of the active profile (empty string means custom/manual).
    /// </summary>
    public static string ActiveProfile
    {
        get => Settings.Values["ActiveProfile"] as string ?? "Chat";
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

    /// <summary>
    /// Gets or sets the last time built-in server tools were checked for updates (UTC).
    /// Used to throttle NuGet update checks to once per 24 hours in Release builds.
    /// </summary>
    public static DateTime? LastToolUpdateCheck
    {
        get => Settings.Values["LastToolUpdateCheck"] is long ticks
            ? new DateTime(ticks, DateTimeKind.Utc)
            : null;
        set => Settings.Values["LastToolUpdateCheck"] = value?.Ticks;
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
        // --- No-tool conversational profile ---
        new()
        {
            Name = "Chat",
            IsBuiltIn = true,
            EnabledServers = [],
            SystemPrompt =
                "You are a friendly, thoughtful conversational assistant. " +
                "Provide clear and helpful responses. " +
                "If you're unsure about something, say so honestly."
        },
        // --- General-purpose with tool access ---
        new()
        {
            Name = "General",
            IsBuiltIn = true,
            EnabledServers = ["builtin_essentials", "builtin_filesystem", "builtin_outbox", "builtin_runner"],
            SystemPrompt =
                "You are a helpful assistant. Provide clear, well-structured responses. " +
                "Explain step by step when needed. If you're unsure about something, say so honestly. " +
                "You have access to tools — use them proactively when they would help answer the user's question. " +
                "You can send messages through configured channels (Slack, Telegram, Email, KakaoTalk) " +
                "using Outbox tools. Use list_channels to check available channels."
        },
        // --- Code & data focused, aggressive tool use ---
        new()
        {
            Name = "Analytical",
            IsBuiltIn = true,
            EnabledServers = ["builtin_essentials", "builtin_filesystem", "builtin_runner"],
            SystemPrompt =
                "You are a concise, direct assistant focused on code and data analysis. " +
                "Prioritize code readability and performance. Use Markdown formatting. " +
                "Always prefer running code and showing actual results over explaining what to do. " +
                "Use search_files to explore the workspace before making assumptions about project structure."
        },
        // --- Creative exploration ---
        new()
        {
            Name = "Creative",
            IsBuiltIn = true,
            EnabledServers = ["builtin_essentials"],
            SystemPrompt =
                "You are a creative assistant who explores multiple perspectives. " +
                "Suggest innovative ideas and ask follow-up questions to better understand the user's needs. " +
                "You can fetch web pages for inspiration and reference when needed."
        },
        // --- Multi-step task execution ---
        new()
        {
            Name = "Task Planner",
            IsBuiltIn = true,
            EnabledServers = ["builtin_essentials", "builtin_filesystem", "builtin_outbox", "builtin_runner"],
            SystemPrompt =
                "You are a task planner that breaks down complex requests into steps and executes them using available tools. " +
                "For complex or multi-step tasks, present a numbered plan and wait for the user to approve before proceeding. " +
                "For simple tasks, execute directly without unnecessary ceremony. " +
                "Report progress after each major step. If a step fails, explain what went wrong and suggest alternatives. " +
                "You can send notifications through messaging channels (Slack, Telegram, Email, KakaoTalk) " +
                "using Outbox tools. Use list_channels to check available channels. " +
                "Always prefer the simplest approach. Do not over-engineer."
        },
        // --- RAG-powered Q&A ---
        new()
        {
            Name = "Knowledge Base",
            IsBuiltIn = true,
            EnabledServers = ["builtin_essentials", "builtin_filesystem", "builtin_rag", "builtin_runner"],
            SystemPrompt =
                "You are a helpful assistant with access to a knowledge base. " +
                "When the question may relate to indexed content, search the knowledge base first using search_documents. " +
                "Cite the source document when providing information from the knowledge base. " +
                "If the knowledge base has no relevant results, say so and answer from your general knowledge."
        }
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
        ("Claude", "Anthropic Claude", null, "claude-sonnet-4-6"),
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
    /// Returns the static default for a built-in profile by name, or <c>null</c> if not found.
    /// </summary>
    public static Profile? GetBuiltInDefaults(string name)
        => BuiltInProfiles.FirstOrDefault(p => p.Name == name);

    /// <summary>
    /// Loads all profiles, combining built-in profiles with user-created custom profiles from storage.
    /// </summary>
    /// <returns>A list of all available profiles.</returns>
    /// <summary>
    /// Forces the next <see cref="LoadProfiles"/> call to reload from disk.
    /// </summary>
    public static void InvalidateProfileCache() => _profileCache = null;

    /// <summary>
    /// Loads all profiles, combining built-in profiles with user-created custom profiles from storage.
    /// </summary>
    /// <returns>A list of all available profiles.</returns>
    public static List<Profile> LoadProfiles()
    {
        if (_profileCache is not null)
            return _profileCache;

        // Clone built-in profiles so modifications don't affect the static defaults
        var result = BuiltInProfiles.Select(p => new Profile
        {
            Name = p.Name,
            SystemPrompt = p.SystemPrompt,
            IsBuiltIn = p.IsBuiltIn,
            PreferredProviderType = p.PreferredProviderType,
            PreferredModelId = p.PreferredModelId,
            ToolNames = [.. p.ToolNames],
            UseSearchTools = p.UseSearchTools,
            EnabledServers = [.. p.EnabledServers],
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
                        target.EnabledServers = ov.EnabledServers;
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
                var builtInNames = result.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                result.AddRange(custom.Where(c => !builtInNames.Contains(c.Name)));
            }
            catch { /* ignore corrupt data */ }
        }

        // Migration: convert legacy ToolNames to EnabledServers["builtin_essentials"]
        var essentialsId = $"builtin_{Mcp.BuiltInServerHelper.EssentialsKey}";
        foreach (var p in result)
        {
            if (p.ToolNames.Count > 0 && !p.EnabledServers.Contains(essentialsId))
            {
                p.EnabledServers.Insert(0, essentialsId);
                p.ToolNames.Clear();
            }

            // Migration: "essentials" → "builtin_essentials"
            var legacyIdx = p.EnabledServers.IndexOf(Mcp.BuiltInServerHelper.EssentialsKey);
            if (legacyIdx >= 0 && !p.EnabledServers.Contains(essentialsId))
            {
                p.EnabledServers[legacyIdx] = essentialsId;
            }
            else if (legacyIdx >= 0)
            {
                p.EnabledServers.RemoveAt(legacyIdx);
            }

            // Migration: remove "memory" — now part of Essentials MCP server
            p.EnabledServers.Remove(Mcp.BuiltInServerHelper.MemoryKey);
        }

        _profileCache = result;
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
            .Select(p => new Profile { Name = p.Name, ToolNames = p.ToolNames, UseSearchTools = p.UseSearchTools, EnabledServers = p.EnabledServers })
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

    /// <summary>
    /// Records that the /models endpoint is unavailable for the specified provider.
    /// Prevents repeated 404 requests for custom providers without a models listing API.
    /// </summary>
    public static void SetModelsEndpointFailed(string provider)
        => Settings.Values[$"ModelsEndpointFailed_{provider}"] = true;

    /// <summary>
    /// Returns whether the /models endpoint has previously failed for the specified provider.
    /// </summary>
    public static bool GetModelsEndpointFailed(string provider)
        => Settings.Values[$"ModelsEndpointFailed_{provider}"] is true;

    /// <summary>
    /// Clears the /models failure flag, allowing a fresh retry (e.g. after re-adding an API key).
    /// </summary>
    public static void ClearModelsEndpointFailed(string provider)
        => Settings.Values.Remove($"ModelsEndpointFailed_{provider}");

    #endregion

    #region Provider Model Methods

    /// <summary>In-memory model cache to avoid repeated PasswordVault reads.</summary>
    private static ObservableCollection<ProviderModel>? _modelsCache;

    /// <summary>
    /// Persists provider models to local storage, saving API keys securely via the password vault.
    /// </summary>
    public static void SaveModels(IList<ProviderModel> models)
    {
        // Save API keys to PasswordVault, serialize rest to JSON
        foreach (var p in models)
        {
            if (p.RequiresApiKey && !string.IsNullOrEmpty(p.ApiKey))
            {
                PasswordVaultHelper.SaveApiKey(p.ProviderType, p.ApiKey);
            }
        }

        // Sync to Win32 Credential Manager for external processes (Runner)
        PasswordVaultHelper.SyncToCredentialManager();

        var list = models as List<ProviderModel> ?? [.. models];
        var json = JsonSerializer.Serialize(list, AppJsonContext.Default.ListProviderModel);
        Settings.Values["ProviderModels"] = json;

        // Update cache with the just-saved models (already have API keys in memory)
        if (list.Count > 0)
            _modelsCache = new ObservableCollection<ProviderModel>(list);

        ModelsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Loads provider models from local storage, restoring API keys from the password vault.
    /// Falls back to building models from vault keys if no saved models exist.
    /// Migrates from the legacy "ProviderPresets" key (one entry per provider) into the
    /// new "ProviderModels" key (N entries per provider — one per enabled model).
    /// Idempotent: subsequent calls find the new key directly.
    /// Uses an in-memory cache to avoid repeated slow PasswordVault reads.
    /// </summary>
    /// <returns>An observable collection of provider models.</returns>
    public static ObservableCollection<ProviderModel> LoadModels()
    {
        if (_modelsCache is not null)
            return new ObservableCollection<ProviderModel>(_modelsCache);

        var json = Settings.Values["ProviderModels"] as string;
        ObservableCollection<ProviderModel>? result = null;

        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                var list = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListProviderModel);
                if (list is { Count: > 0 })
                {
                    foreach (var p in list)
                    {
                        if (p.RequiresApiKey)
                        {
                            p.ApiKey = PasswordVaultHelper.LoadApiKey(p.ProviderType);
                        }
                    }

                    result = new ObservableCollection<ProviderModel>(list);
                }
            }
            catch { /* fall through to rebuild */ }
        }

        // Try legacy "ProviderPresets" migration before falling back to vault scan.
        if (result is null)
        {
            result = TryMigrateLegacyProviderPresets();
        }

        // No saved models — build from PasswordVault API keys
        result ??= BuildModelsFromVault();

        // Append synthetic entries for custom providers
        AppendCustomProviderModels(result);

        _modelsCache = result;
        return new ObservableCollection<ProviderModel>(result);
    }

    /// <summary>
    /// Reads the legacy "ProviderPresets" key (one entry per provider), expands each
    /// legacy entry into N <see cref="ProviderModel"/> instances using cached model
    /// lists, persists the result under the new "ProviderModels" key, and removes
    /// the legacy key. Returns <c>null</c> if no legacy data exists.
    /// </summary>
    private static ObservableCollection<ProviderModel>? TryMigrateLegacyProviderPresets()
    {
        var legacyJson = Settings.Values["ProviderPresets"] as string;
        if (string.IsNullOrEmpty(legacyJson)) return null;

        List<LegacyProviderPreset>? legacy;
        try
        {
            legacy = JsonSerializer.Deserialize(legacyJson, AppJsonContext.Default.ListLegacyProviderPreset);
        }
        catch
        {
            return null;
        }

        if (legacy is null || legacy.Count == 0) return null;

        var result = new ObservableCollection<ProviderModel>();
        foreach (var preset in legacy)
        {
            var enabledModels = GetCachedModels(preset.ProviderType)
                ?? new List<string> { preset.ModelId };

            // Patch #2 (활성 모델 보존): the cached model list may not contain
            // the user's last-active ModelId. Always include it so the active
            // selection survives migration; promote it to the front so callers
            // that pick the first entry as default keep the prior selection.
            if (!string.IsNullOrEmpty(preset.ModelId) && !enabledModels.Contains(preset.ModelId))
                enabledModels = enabledModels.Prepend(preset.ModelId).ToList();
            else
                enabledModels = enabledModels
                    .OrderByDescending(id => id == preset.ModelId)
                    .ToList();

            foreach (var modelId in enabledModels)
            {
                var model = new ProviderModel
                {
                    Name = modelId,
                    ProviderType = preset.ProviderType,
                    ModelId = modelId,
                    BaseUrl = preset.BaseUrl,
                    MaxTokens = preset.MaxTokens,
                    Temperature = preset.Temperature,
                    StreamingEnabled = preset.StreamingEnabled,
                    PdfCapability = preset.PdfCapability,
                    ThinkingEnabled = preset.ThinkingEnabled,
                    ThinkingOverride = preset.ThinkingOverride,
                    ThinkingBudget = preset.ThinkingBudget,
                    // Per-model fields: only the legacy entry's values populate
                    // the matching ModelId entry; others get nullable defaults.
                    KeepAlive = modelId == preset.ModelId ? preset.KeepAlive : null,
                    NumCtx = modelId == preset.ModelId ? preset.NumCtx : null,
                };
                if (model.RequiresApiKey)
                    model.ApiKey = PasswordVaultHelper.LoadApiKey(model.ProviderType);
                result.Add(model);
            }
        }

        SaveModels(result);
        Settings.Values.Remove("ProviderPresets");
        return result;
    }

    /// <summary>
    /// Appends synthetic <see cref="ProviderModel"/> entries for each registered custom provider.
    /// </summary>
    private static void AppendCustomProviderModels(ObservableCollection<ProviderModel> models)
    {
        var customs = LoadCustomProviders();
        foreach (var config in customs)
        {
            var providerType = $"Custom_{config.Id}";

            // Skip if already present (e.g., from serialized models)
            if (models.Any(p => p.ProviderType == providerType))
                continue;

            models.Add(new ProviderModel
            {
                Name = config.DisplayName,
                ProviderType = providerType,
                BaseUrl = config.BaseUrl,
                ModelId = GetDefaultModel(providerType) ?? "",
                ApiKey = PasswordVaultHelper.LoadApiKey(providerType),
            });
        }
    }

    /// <summary>
    /// Builds an ordered list of model items grouped by category (Cloud → Custom → Local → Demo)
    /// with separator markers ("-") between non-empty groups.
    /// </summary>
    /// <returns>A list containing <see cref="ProviderModel"/> objects and separator strings.</returns>
    public static ArrayList BuildOrderedModelItems()
    {
        var models = LoadModels();

        var cloud = models.Where(p => IsCloudProvider(p.ProviderType)).ToList();
        var custom = models.Where(p => p.ProviderType.StartsWith("Custom_")).ToList();
        var local = models.Where(p => p.ProviderType == "Ollama").ToList();
        var demo = models.Where(p => p.ProviderType == "Mock").ToList();

        var result = new ArrayList();
        AddGroup(result, cloud);
        AddGroup(result, custom);
        AddGroup(result, local);
        AddGroup(result, demo);
        return result;

        static void AddGroup(ArrayList list, List<ProviderModel> group)
        {
            if (group.Count == 0) return;
            if (list.Count > 0) list.Add("-");
            list.AddRange(group);
        }
    }

    /// <summary>
    /// Determines whether the given provider type is a known cloud provider.
    /// </summary>
    private static bool IsCloudProvider(string type)
        => type is "Claude" or "OpenAI" or "Gemini" or "Groq";

    #endregion

    #region Built-in Server Methods

    /// <summary>
    /// Raised when built-in server configurations change.
    /// </summary>
    public static event EventHandler? BuiltInServersChanged;

    /// <summary>
    /// Gets or sets the default built-in MCP server configurations.
    /// Applied to all new conversations unless overridden per-conversation.
    /// </summary>
    public static Dictionary<string, BuiltInServerConfig> BuiltInServers
    {
        get
        {
            var json = Settings.Values["BuiltInServers"] as string;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    return JsonSerializer.Deserialize(json,
                        AppJsonContext.Default.DictionaryStringBuiltInServerConfig) ?? Mcp.BuiltInServerHelper.GetDefaults();
                }
                catch { /* fall through */ }
            }
            return Mcp.BuiltInServerHelper.GetDefaults();
        }
        set
        {
            var json = JsonSerializer.Serialize(value,
                AppJsonContext.Default.DictionaryStringBuiltInServerConfig);
            Settings.Values["BuiltInServers"] = json;
            BuiltInServersChanged?.Invoke(null, EventArgs.Empty);
        }
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

    #region Custom Provider Methods

    /// <summary>Raised when custom provider configurations change.</summary>
    public static event EventHandler? CustomProvidersChanged;

    /// <summary>In-memory cache of custom provider configs.</summary>
    private static List<CustomProviderConfig>? _customProviderCache;

    /// <summary>Gets the path to the custom providers configuration file.</summary>
    private static string CustomProvidersFilePath
        => Path.Combine(ApplicationData.Current.LocalFolder.Path, "custom-providers.json");

    /// <summary>
    /// Loads custom provider configurations from the JSON file.
    /// Must be called at app startup before <see cref="LoadModels"/>.
    /// </summary>
    public static List<CustomProviderConfig> LoadCustomProviders()
    {
        if (_customProviderCache is not null)
            return _customProviderCache;

        try
        {
            if (!File.Exists(CustomProvidersFilePath))
            {
                _customProviderCache = [];
                return _customProviderCache;
            }

            var json = File.ReadAllText(CustomProvidersFilePath);
            _customProviderCache = JsonSerializer.Deserialize(json, AppJsonContext.Default.ListCustomProviderConfig) ?? [];
            return _customProviderCache;
        }
        catch
        {
            _customProviderCache = [];
            return _customProviderCache;
        }
    }

    /// <summary>
    /// Saves custom provider configurations to the JSON file.
    /// </summary>
    public static void SaveCustomProviders(List<CustomProviderConfig> configs)
    {
        var json = JsonSerializer.Serialize(configs, AppJsonContext.Default.ListCustomProviderConfig);
        File.WriteAllText(CustomProvidersFilePath, json);

        _customProviderCache = configs;
        _modelsCache = null; // Invalidate model cache so synthetic entries are regenerated
        CustomProvidersChanged?.Invoke(null, EventArgs.Empty);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Builds a default set of provider models by scanning the password vault for stored API keys.
    /// Always includes Ollama and Mock providers.
    /// </summary>
    /// <returns>An observable collection of provider models constructed from vault data.</returns>
    private static ObservableCollection<ProviderModel> BuildModelsFromVault()
    {
        var models = new ObservableCollection<ProviderModel>();

        foreach (var (providerType, displayName, baseUrl, fallbackModel) in KnownProviders)
        {
            var key = PasswordVaultHelper.LoadApiKey(providerType);
            if (string.IsNullOrEmpty(key)) continue;

            var savedModel = GetDefaultModel(providerType);

            models.Add(new ProviderModel
            {
                Name = displayName,
                ProviderType = providerType,
                ModelId = savedModel ?? fallbackModel,
                ApiKey = key,
                BaseUrl = baseUrl,
            });
        }

        // Ollama (local, may not be running)
        var ollamaModel = GetDefaultModel("Ollama");
        models.Add(new ProviderModel
        {
            Name = "Ollama",
            ProviderType = "Ollama",
            ModelId = ollamaModel ?? "llama3.1",
            BaseUrl = GetOllamaBaseUrl(),
        });

        // Mock
        models.Add(new ProviderModel { Name = "Mock", ProviderType = "Mock" });

        return models;
    }

    #endregion

    #region Specialist Settings

    /// <summary>
    /// Gets or sets whether the Web Search Specialist is enabled.
    /// When enabled, the routing guideline is injected into the system prompt
    /// and delegate_task calls with specialist="web_search_specialist" are auto-approved.
    /// </summary>
    public static bool WebSearchSpecialistEnabled
    {
        get => Settings.Values["WebSearchSpecialistEnabled"] is not false;
        set => Settings.Values["WebSearchSpecialistEnabled"] = value;
    }

    /// <summary>
    /// Gets or sets the provider source for the named specialist
    /// ("Inherit" or "Specific"). Inherit means fall back to the
    /// general Sub-Agent setting / parent conversation provider.
    /// </summary>
    public static string GetSpecialistSource(string specialistName)
        => Settings.Values[$"Specialist_{specialistName}_Source"] as string ?? "Inherit";

    /// <summary>
    /// Sets the provider source for the named specialist.
    /// </summary>
    public static void SetSpecialistSource(string specialistName, string source)
    {
        Settings.Values[$"Specialist_{specialistName}_Source"] = source;
        TaskSettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Gets or sets the model name for the named specialist when
    /// <see cref="GetSpecialistSource"/> is "Specific".
    /// </summary>
    public static string GetSpecialistModel(string specialistName)
        => Settings.Values[$"Specialist_{specialistName}_Model"] as string ?? "";

    /// <summary>
    /// Sets the model name for the named specialist.
    /// </summary>
    public static void SetSpecialistModel(string specialistName, string model)
    {
        Settings.Values[$"Specialist_{specialistName}_Model"] = model;
        TaskSettingsChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Resolves the provider model for a specialist using the fallback
    /// chain: per-specialist override → general sub-agent setting →
    /// <see langword="null"/> (parent conversation provider).
    /// </summary>
    public static string? ResolveSpecialistModel(string specialistName)
    {
        // 1. Per-specialist override (only when Source == "Specific")
        if (GetSpecialistSource(specialistName) == "Specific")
        {
            var model = GetSpecialistModel(specialistName);
            if (!string.IsNullOrEmpty(model))
                return model;
        }

        // 2. General sub-agent default
        if (SubAgentSource == "Specific" && !string.IsNullOrEmpty(SubAgentModel))
            return SubAgentModel;

        // 3. Parent conversation model (null = inherit)
        return null;
    }

    #endregion

    #region Legacy DTO

    /// <summary>
    /// Legacy data shape for the deprecated "ProviderPresets" storage key.
    /// Used solely by <see cref="TryMigrateLegacyProviderPresets"/> to read
    /// pre-PR-1 settings and expand them into the new
    /// <see cref="ProviderModel"/> list.
    /// </summary>
    public sealed class LegacyProviderPreset
    {
        /// <summary>The legacy display name.</summary>
        public string Name { get; set; } = "";

        /// <summary>Provider type key.</summary>
        public string ProviderType { get; set; } = "Mock";

        /// <summary>Model identifier last selected for this provider.</summary>
        public string ModelId { get; set; } = "";

        /// <summary>Optional override base URL.</summary>
        public string? BaseUrl { get; set; }

        /// <summary>Sampling temperature.</summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>Maximum response tokens.</summary>
        public int MaxTokens { get; set; } = 4096;

        /// <summary>Whether streaming is enabled.</summary>
        public bool StreamingEnabled { get; set; } = true;

        /// <summary>PDF document handling capability.</summary>
        public PdfCapability PdfCapability { get; set; } = PdfCapability.Auto;

        /// <summary>Whether thinking/reasoning is enabled.</summary>
        public bool ThinkingEnabled { get; set; }

        /// <summary>Thinking budget in tokens.</summary>
        public int? ThinkingBudget { get; set; }

        /// <summary>Thinking-support override.</summary>
        public ThinkingOverride ThinkingOverride { get; set; } = ThinkingOverride.Auto;

        /// <summary>Ollama keep-alive duration.</summary>
        public string? KeepAlive { get; set; }

        /// <summary>Ollama context window size.</summary>
        public int? NumCtx { get; set; }
    }

    #endregion
}
