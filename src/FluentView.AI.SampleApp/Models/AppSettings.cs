using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.Storage;

using FluentView.AI.Helpers;
using FluentView.AI.Models;

namespace FluentView.AI.SampleApp.Models;

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
        if (string.IsNullOrEmpty(json))
        {
            return [new ProviderPreset { Name = "Mock", ProviderType = "Mock" }];
        }

        try
        {
            var list = JsonSerializer.Deserialize<List<ProviderPreset>>(json)
                       ?? [new ProviderPreset { Name = "Mock", ProviderType = "Mock" }];

            foreach (var p in list)
            {
                if (p.RequiresApiKey)
                {
                    p.ApiKey = PasswordVaultHelper.LoadApiKey(p.Name);
                }
            }

            return new ObservableCollection<ProviderPreset>(list);
        }
        catch
        {
            return [new ProviderPreset { Name = "Mock", ProviderType = "Mock" }];
        }
    }
}
