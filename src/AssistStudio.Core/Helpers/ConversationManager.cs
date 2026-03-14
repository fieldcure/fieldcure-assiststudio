using System.Text.Json;
using System.Text.Json.Serialization;
using FieldCure.AssistStudio.Models;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Provides methods for saving, loading, and managing chat conversation files.
/// </summary>
public static class ConversationManager
{
    #region Constants

    /// <summary>The file extension used for conversation files.</summary>
    public const string FileExtension = ".astx";

    #endregion

    #region Fields

    /// <summary>The folder path where conversations are stored.</summary>
    private static string _conversationsFolder = "";

    /// <summary>Shared JSON serializer options for conversation serialization.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    #endregion

    #region Public Methods

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

    /// <summary>
    /// Loads a conversation from the specified file path.
    /// </summary>
    public static async Task<ConversationData?> LoadConversationAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        var json = await File.ReadAllTextAsync(filePath);
        return JsonSerializer.Deserialize<ConversationData>(json, JsonOptions);
    }

    /// <summary>
    /// Lists saved conversations in the conversations folder, ordered by most recently modified.
    /// </summary>
    public static IReadOnlyList<ConversationFileInfo> ListSavedConversations(int top = int.MaxValue)
    {
        EnsureInitialized();
        if (!Directory.Exists(_conversationsFolder))
            return [];

        var astxFiles = Directory.GetFiles(_conversationsFolder, "*" + FileExtension);
        var jsonFiles = Directory.GetFiles(_conversationsFolder, "*.json");

        return astxFiles.Concat(jsonFiles)
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

    /// <summary>
    /// Deletes all saved conversation files from the conversations folder.
    /// </summary>
    public static void ClearAll()
    {
        EnsureInitialized();
        if (!Directory.Exists(_conversationsFolder)) return;

        var files = Directory.GetFiles(_conversationsFolder, "*" + FileExtension)
            .Concat(Directory.GetFiles(_conversationsFolder, "*.json"));

        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch { /* ignore individual file deletion errors */ }
        }
    }

    #endregion

    #region Private Methods

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

    /// <summary>Sanitizes a file name by replacing invalid characters with underscores.</summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }

    /// <summary>Throws if <see cref="Initialize"/> has not been called.</summary>
    private static void EnsureInitialized()
    {
        if (string.IsNullOrEmpty(_conversationsFolder))
            throw new InvalidOperationException(
                "ConversationManager.Initialize() must be called before use.");
    }

    #endregion
}

/// <summary>
/// The serialized data structure for a saved conversation file.
/// </summary>
public class ConversationData
{
    #region Properties

    /// <summary>The JSON schema URI for the conversation format.</summary>
    [JsonPropertyName("$schema")]
    public string Schema { get; set; } = "https://assiststudio.dev/schema/conversation/v1";

    /// <summary>The type discriminator for the conversation format.</summary>
    [JsonPropertyName("$type")]
    public string Type { get; set; } = "AssistStudio.Conversation";

    /// <summary>The schema version of the conversation format.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>The display name of the tab this conversation was saved from.</summary>
    public string TabName { get; set; } = "";

    /// <summary>The name of the provider preset used for this conversation.</summary>
    public string? ProviderPresetName { get; set; }

    /// <summary>The UTC timestamp when the conversation was saved.</summary>
    public DateTime SavedAt { get; set; }

    /// <summary>The list of messages in the conversation.</summary>
    public List<SavedMessage> Messages { get; set; } = [];

    #endregion
}

/// <summary>
/// Represents a single message within a saved conversation.
/// </summary>
public class SavedMessage
{
    #region Properties

    /// <summary>The role of the message sender.</summary>
    public ChatRole Role { get; set; }

    /// <summary>The text content of the message.</summary>
    public string Content { get; set; } = "";

    /// <summary>The provider name that generated this message, if it was an assistant response.</summary>
    public string? ProviderName { get; set; }

    /// <summary>The model identifier used to generate this message, if it was an assistant response.</summary>
    public string? ProviderModelId { get; set; }

    /// <summary>The UTC timestamp when the message was created.</summary>
    public DateTime Timestamp { get; set; }

    #endregion
}

/// <summary>
/// Provides metadata about a saved conversation file on disk.
/// </summary>
public class ConversationFileInfo
{
    #region Properties

    /// <summary>The full file path of the conversation file.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>The file name without extension.</summary>
    public string FileName { get; set; } = "";

    /// <summary>The last modified timestamp of the file in UTC.</summary>
    public DateTime ModifiedAt { get; set; }

    #endregion
}
