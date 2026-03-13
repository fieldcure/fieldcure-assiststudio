using System.Text.Json;
using System.Text.Json.Serialization;
using FluentView.AI.Models;

namespace FluentView.AI.Helpers;

public static class ConversationManager
{
    private static string _conversationsFolder = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Initialize the conversation manager with a base folder path.
    /// Must be called before any save/load operations.
    /// </summary>
    public static void Initialize(string baseFolderPath)
    {
        _conversationsFolder = Path.Combine(baseFolderPath, "Conversations");
        Directory.CreateDirectory(_conversationsFolder);
    }

    public static async Task SaveConversationAsync(
        string tabName,
        string? providerPresetName,
        IReadOnlyList<ChatMessage> messages)
    {
        EnsureInitialized();
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
        var filePath = Path.Combine(_conversationsFolder, fileName);
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    public static async Task<ConversationData?> LoadConversationAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ConversationData>(json, JsonOptions);
    }

    public static IReadOnlyList<ConversationFileInfo> ListSavedConversations(int top = int.MaxValue)
    {
        EnsureInitialized();
        if (!Directory.Exists(_conversationsFolder))
            return [];

        return Directory.GetFiles(_conversationsFolder, "*.json")
            .Select(f => new ConversationFileInfo
            {
                FilePath = f,
                FileName = Path.GetFileNameWithoutExtension(f),
                ModifiedAt = File.GetLastWriteTimeUtc(f),
            })
            .OrderByDescending(f => f.ModifiedAt)
            .Take(top)
            .ToList();
    }

    public static void ClearAll()
    {
        EnsureInitialized();
        if (!Directory.Exists(_conversationsFolder)) return;

        foreach (var file in Directory.GetFiles(_conversationsFolder, "*.json"))
        {
            try { File.Delete(file); }
            catch { /* ignore individual file deletion errors */ }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    private static void EnsureInitialized()
    {
        if (string.IsNullOrEmpty(_conversationsFolder))
            throw new InvalidOperationException(
                "ConversationManager.Initialize() must be called before use.");
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
