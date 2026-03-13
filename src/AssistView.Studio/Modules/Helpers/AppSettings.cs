using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.Storage;

using FluentView.AI.Helpers;
using FluentView.AI.Models;

namespace AssistView.Studio.Helpers;

public static class AppSettings
{
    private static ApplicationDataContainer Settings => ApplicationData.Current.LocalSettings;

    public static bool IsFirstRun
    {
        get => Settings.Values["IsFirstRun"] is not false;
        set => Settings.Values["IsFirstRun"] = value;
    }

    public static string Theme
    {
        get => Settings.Values["Theme"] as string ?? "System";
        set => Settings.Values["Theme"] = value;
    }

    public static string SystemPrompt
    {
        get => Settings.Values["SystemPrompt"] as string ?? "";
        set => Settings.Values["SystemPrompt"] = value;
    }

    public static string DefaultProvider
    {
        get => Settings.Values["DefaultProvider"] as string ?? "Mock";
        set => Settings.Values["DefaultProvider"] = value;
    }

    public static string? GetDefaultModel(string provider)
    {
        return Settings.Values[$"DefaultModel_{provider}"] as string;
    }

    public static void SetDefaultModel(string provider, string modelId)
    {
        Settings.Values[$"DefaultModel_{provider}"] = modelId;
    }

    // Utility AI settings
    public static string UtilityAISource
    {
        get => Settings.Values["UtilityAISource"] as string ?? "Current";
        set => Settings.Values["UtilityAISource"] = value;
    }

    public static string UtilityAIPreset
    {
        get => Settings.Values["UtilityAIPreset"] as string ?? "";
        set => Settings.Values["UtilityAIPreset"] = value;
    }

    public static bool UtilityAutoTitle
    {
        get => Settings.Values["UtilityAutoTitle"] is not false;
        set => Settings.Values["UtilityAutoTitle"] = value;
    }

    public static bool UtilityAutoSummary
    {
        get => Settings.Values["UtilityAutoSummary"] is not false;
        set => Settings.Values["UtilityAutoSummary"] = value;
    }

    // Active prompt preset name (empty = custom/manual)
    public static string ActivePromptPreset
    {
        get => Settings.Values["ActivePromptPreset"] as string ?? "Professional";
        set => Settings.Values["ActivePromptPreset"] = value;
    }

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

    public static void SaveCustomPromptPresets(IEnumerable<PromptPreset> allPresets)
    {
        var custom = allPresets.Where(p => !p.IsBuiltIn).ToList();
        var json = JsonSerializer.Serialize(custom);
        Settings.Values["CustomPromptPresets"] = json;
    }

    // ===== MRU (Most Recently Used) File Paths =====

    private const int MaxRecentFiles = 10;

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

    public static void AddRecentFile(string filePath)
    {
        var list = RecentFilePaths;
        list.Remove(filePath);
        list.Insert(0, filePath);
        if (list.Count > MaxRecentFiles)
            list.RemoveRange(MaxRecentFiles, list.Count - MaxRecentFiles);
        RecentFilePaths = list;
    }

    // ===== Model Cache =====

    private static readonly TimeSpan ModelCacheExpiry = TimeSpan.FromHours(24);

    public static void SetCachedModels(string provider, List<string> modelIds)
    {
        var json = JsonSerializer.Serialize(modelIds);
        Settings.Values[$"CachedModels_{provider}"] = json;
        Settings.Values[$"CachedModels_{provider}_At"] = DateTimeOffset.UtcNow.Ticks;
    }

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

    private static readonly (string ProviderType, string DisplayName, string? BaseUrl, string FallbackModel)[] KnownProviders =
    [
        ("Claude", "Anthropic Claude", null, "claude-sonnet-4-20250514"),
        ("OpenAI", "OpenAI", null, "gpt-4o"),
        ("Gemini", "Google Gemini", null, "gemini-2.0-flash"),
        ("Groq", "Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
    ];

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
}
