using System.Collections.ObjectModel;
using System.Text.Json;
using Windows.Storage;

using FluentView.AI.SampleApp.Helpers;

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
