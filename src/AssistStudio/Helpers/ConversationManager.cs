using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using AssistStudio.Models;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;

namespace AssistStudio.Helpers;

/// <summary>
/// Provides methods for saving, loading, and managing chat conversation files.
/// </summary>
public static class ConversationManager
{
    #region Constants

    /// <summary>The file extension used for conversation files.</summary>
    public const string FileExtension = ".astd";

    #endregion

    #region Fields

    /// <summary>The folder path where conversations are stored.</summary>
    private static string _conversationsFolder = "";

    /// <summary>Shared JSON type info for indented conversation serialization.</summary>
    private static readonly JsonTypeInfo<ConversationData> ConversationTypeInfo =
        IndentedJsonContext.Default.ConversationData;

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
        IReadOnlyList<ChatMessage> messages,
        string? activeRootChildId = null,
        Dictionary<string, BuiltInServerConfig>? builtInServers = null,
        string? conversationId = null)
    {
        EnsureInitialized();
        if (messages.Count == 0) return;

        var fileName = SanitizeFileName(tabName) + FileExtension;
        var filePath = Path.Combine(_conversationsFolder, fileName);
        await SaveToFileAsync(filePath, tabName, providerPresetName, messages, activeRootChildId, builtInServers, conversationId);
    }

    /// <summary>
    /// Save conversation to an arbitrary file path.
    /// </summary>
    public static async Task SaveToFileAsync(
        string filePath,
        string tabName,
        string? providerPresetName,
        IReadOnlyList<ChatMessage> messages,
        string? activeRootChildId = null,
        Dictionary<string, BuiltInServerConfig>? builtInServers = null,
        string? conversationId = null)
    {
        if (messages.Count == 0) return;

        // Ensure a stable conversation ID for media storage
        conversationId ??= Guid.NewGuid().ToString("N");

        var savedMessages = new List<SavedMessage>(messages.Count);
        foreach (var m in messages)
        {
            var saved = new SavedMessage
            {
                Id = m.Id,
                Role = m.Role,
                Content = m.Content,
                ProviderName = m.ProviderName,
                ProviderModelId = m.ProviderModelId,
                Timestamp = m.Timestamp,
                ToolCalls = m.ToolCalls,
                ToolCallId = m.ToolCallId,
                ParentId = m.ParentId,
                ActiveChildId = m.ActiveChildId,
                ThinkingContent = m.ThinkingContent,
            };

            // Persist user image attachments
            if (m.Role == ChatRole.User && m.Attachments is { Count: > 0 })
            {
                foreach (var att in m.Attachments.Where(a => a.Type == AttachmentType.Image))
                {
                    var fileName = await MediaStore.SaveAsync(
                        conversationId, att.FileName, att.Data);
                    saved.Media ??= [];
                    saved.Media.Add(new MediaReference
                    {
                        Id = $"upload_{saved.Media.Count}",
                        FileName = fileName,
                        MimeType = att.MimeType ?? "image/png",
                        Source = "user_upload"
                    });
                }
            }

            // Persist tool result media (images from MCP tools, etc.)
            if (m.Role == ChatRole.Tool && m.ToolMedia is { Count: > 0 })
            {
                foreach (var media in m.ToolMedia)
                {
                    var bytes = DecodeMediaUri(media.MediaUri);
                    if (bytes is null) continue;

                    var ext = MimeToExtension(media.MimeType);
                    var fileName = await MediaStore.SaveAsync(
                        conversationId, $"tool_{m.ToolCallId}{ext}", bytes);
                    saved.Media ??= [];
                    saved.Media.Add(new MediaReference
                    {
                        Id = $"tool_{saved.Media.Count}",
                        FileName = fileName,
                        MimeType = media.MimeType,
                        Source = "mcp_tool",
                        ToolCallId = m.ToolCallId
                    });
                }
            }

            savedMessages.Add(saved);
        }

        var data = new ConversationData
        {
            TabName = tabName,
            ProviderPresetName = providerPresetName,
            ConversationId = conversationId,
            ActiveRootChildId = activeRootChildId,
            BuiltInServers = builtInServers,
            SavedAt = DateTime.UtcNow,
            Messages = savedMessages,
        };

        var json = JsonSerializer.Serialize(data, ConversationTypeInfo);
        await AtomicWriteAsync(filePath, json);
        LoggingService.LogInfo($"[File] Saved: {Path.GetFileName(filePath)}, messages={messages.Count}, conversationId={conversationId}");
    }

    /// <summary>
    /// Loads a conversation from the specified file path.
    /// </summary>
    public static async Task<ConversationData?> LoadConversationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LoggingService.LogWarning($"[File] File not found: {filePath}");
            return null;
        }

        var json = await File.ReadAllTextAsync(filePath);
        var data = JsonSerializer.Deserialize(json, ConversationTypeInfo);
        LoggingService.LogInfo($"[File] Loaded: {Path.GetFileName(filePath)}, messages={data?.Messages.Count ?? 0}");
        return data;
    }

    /// <summary>
    /// Lists saved conversations in the conversations folder, ordered by most recently modified.
    /// </summary>
    public static IReadOnlyList<ConversationFileInfo> ListSavedConversations(int top = int.MaxValue)
    {
        EnsureInitialized();
        if (!Directory.Exists(_conversationsFolder))
            return [];

        return Directory.GetFiles(_conversationsFolder, "*" + FileExtension)
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
    /// Deletes all saved conversation files and associated media from the conversations folder.
    /// </summary>
    public static void ClearAll()
    {
        EnsureInitialized();

        // Delete all media files
        MediaStore.ClearAll();

        if (!Directory.Exists(_conversationsFolder)) return;

        var files = Directory.GetFiles(_conversationsFolder, "*" + FileExtension);

        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch (Exception ex) { LoggingService.LogException(ex); }
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
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    /// <summary>
    /// Decodes a data URI to raw bytes. Returns null for non-data URIs.
    /// </summary>
    private static byte[]? DecodeMediaUri(string mediaUri)
    {
        if (!mediaUri.StartsWith("data:", StringComparison.Ordinal))
            return null;

        var commaIdx = mediaUri.IndexOf(',');
        if (commaIdx < 0) return null;

        try
        {
            return Convert.FromBase64String(mediaUri[(commaIdx + 1)..]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a MIME type to a file extension.
    /// </summary>
    private static string MimeToExtension(string mimeType) => mimeType switch
    {
        "image/png" => ".png",
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "audio/wav" => ".wav",
        "audio/mpeg" => ".mp3",
        "video/mp4" => ".mp4",
        "video/webm" => ".webm",
        _ => ".bin"
    };

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
