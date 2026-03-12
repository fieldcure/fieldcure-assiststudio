using System.Text.Json;
using System.Text.Json.Serialization;
using FluentView.AI.Models;
using Windows.Storage;

namespace FluentView.AI.SampleApp.Helpers;

public static class ConversationManager
{
    private static readonly string ConversationsFolder = Path.Combine(
        ApplicationData.Current.LocalFolder.Path, "Conversations");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static ConversationManager()
    {
        Directory.CreateDirectory(ConversationsFolder);
    }

    public static async Task SaveConversationAsync(
        string tabName,
        string? providerPresetName,
        IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0) return;

        var data = new ConversationData
        {
            TabName = tabName,
            ProviderPresetName = providerPresetName,
            SavedAt = DateTime.UtcNow,
            Messages = messages.Select(m => new SavedMessage
            {
                Role = m.Role,
                Content = m.Content,
                ProviderName = m.ProviderName,
                ProviderModelId = m.ProviderModelId,
                Timestamp = m.Timestamp,
            }).ToList(),
        };

        var fileName = SanitizeFileName(tabName) + ".json";
        var filePath = Path.Combine(ConversationsFolder, fileName);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<ConversationData?> LoadConversationAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ConversationData>(json, JsonOptions);
    }

    public static IReadOnlyList<ConversationFileInfo> ListSavedConversations()
    {
        if (!Directory.Exists(ConversationsFolder))
            return [];

        return Directory.GetFiles(ConversationsFolder, "*.json")
            .Select(f => new ConversationFileInfo
            {
                FilePath = f,
                FileName = Path.GetFileNameWithoutExtension(f),
                ModifiedAt = File.GetLastWriteTimeUtc(f),
            })
            .OrderByDescending(f => f.ModifiedAt)
            .ToList();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

public class ConversationData
{
    public string TabName { get; set; } = "";
    public string? ProviderPresetName { get; set; }
    public DateTime SavedAt { get; set; }
    public List<SavedMessage> Messages { get; set; } = [];
}

public class SavedMessage
{
    public ChatRole Role { get; set; }
    public string Content { get; set; } = "";
    public string? ProviderName { get; set; }
    public string? ProviderModelId { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ConversationFileInfo
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public DateTime ModifiedAt { get; set; }
}
