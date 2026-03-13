using System.Text.Json;
using System.Text.Json.Serialization;
using FluentView.AI.Models;

namespace FluentView.AI.Helpers;

public static class ConversationManager
{
    public const string FileExtension = ".astx";

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

    /// <summary>
    /// Save conversation to the internal Conversations folder.
    /// </summary>
    public static async Task SaveConversationAsync(
        string tabName,
        string? providerPresetName,
        IReadOnlyList<ChatMessage> messages)
    {
        EnsureInitialized();
        if (messages.Count == 0) return;

        var fileName = SanitizeFileName(tabName) + FileExtension;
        var filePath = Path.Combine(_conversationsFolder, fileName);
        await SaveToFileAsync(filePath, tabName, providerPresetName, messages);
    }

    /// <summary>
    /// Save conversation to an arbitrary file path.
    /// </summary>
    public static async Task SaveToFileAsync(
        string filePath,
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

        var json = JsonSerializer.Serialize(data, JsonOptions);
        await AtomicWriteAsync(filePath, json);
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

        var astxFiles = Directory.GetFiles(_conversationsFolder, "*" + FileExtension);
        var avcFiles = Directory.GetFiles(_conversationsFolder, "*.avc");
        var jsonFiles = Directory.GetFiles(_conversationsFolder, "*.json");

        return astxFiles.Concat(avcFiles).Concat(jsonFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
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

        var files = Directory.GetFiles(_conversationsFolder, "*" + FileExtension)
            .Concat(Directory.GetFiles(_conversationsFolder, "*.avc"))
            .Concat(Directory.GetFiles(_conversationsFolder, "*.json"));

        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch { /* ignore individual file deletion errors */ }
        }
    }

    /// <summary>
    /// Write to a temp file first, then atomically replace the target.
    /// Prevents data loss if the process crashes or the write is interrupted
    /// (e.g. enterprise document-centralization agents locking files).
    /// </summary>
    private static async Task AtomicWriteAsync(string filePath, string content)
    {
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        var tempPath = Path.Combine(dir, Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(tempPath, content);
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            try { File.Delete(tempPath); } catch { }
            throw;
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
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "https://assiststudio.dev/schema/conversation/v1";

    [JsonPropertyName("$type")]
    public string Type { get; set; } = "AssistStudio.Conversation";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

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
