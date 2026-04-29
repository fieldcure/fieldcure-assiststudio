using AssistStudio.Models;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Core.Models;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace AssistStudio.Helpers;

/// <summary>
/// Provides methods for saving, loading, and managing chat conversation archives.
/// Files are stored as ZIP-based .astx archives containing manifest.json,
/// conversation.json, and an optional media/ directory for binary attachments.
/// </summary>
public static class ConversationManager
{
    #region Constants

    /// <summary>The file extension used for conversation archives.</summary>
    public const string FileExtension = ".astx";

    /// <summary>The current format version for .astx archives.</summary>
    private const int CurrentFormatVersion = 2;

    /// <summary>The zip entry name used for the manifest JSON inside an .astx archive.</summary>
    private const string ManifestEntryName = "manifest.json";
    /// <summary>The zip entry name used for the conversation body JSON inside an .astx archive.</summary>
    private const string ConversationEntryName = "conversation.json";
    /// <summary>The directory prefix used for media payloads inside an .astx archive.</summary>
    private const string MediaDirPrefix = "media/";

    #endregion

    #region Fields

    /// <summary>The folder path where conversations are stored.</summary>
    private static string _conversationsFolder = "";

    /// <summary>Shared JSON type info for indented conversation serialization.</summary>
    private static readonly JsonTypeInfo<ConversationData> ConversationTypeInfo =
        IndentedJsonContext.Default.ConversationData;

    /// <summary>Shared JSON type info for indented manifest serialization.</summary>
    private static readonly JsonTypeInfo<ManifestData> ManifestTypeInfo =
        IndentedJsonContext.Default.ManifestData;

    /// <summary>Semaphore to ensure only one save runs at a time.</summary>
    private static readonly SemaphoreSlim _saveLock = new(1, 1);

    /// <summary>Cancellation source for the debounce timer. Replaced on each new trigger.</summary>
    private static CancellationTokenSource? _debounceCts;

    /// <summary>Tracks the currently running auto-save task for flush support.</summary>
    private static Task? _pendingAutoSave;

    /// <summary>Debounce interval for auto-save. Short enough to feel instant,
    /// long enough to coalesce rapid-fire triggers (Send + immediate response).</summary>
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(300);

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
    /// Save conversation to an arbitrary file path as a ZIP-based .astx archive.
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

        conversationId ??= Guid.NewGuid().ToString("N");

        // Collect media and build saved messages
        var savedMessages = new List<SavedMessage>(messages.Count);
        var mediaEntries = new Dictionary<string, (byte[] Data, string MimeType)>(); // entryName -> (bytes, mime)

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
                ElapsedSeconds = m.ElapsedSeconds,
                TokenCount = m.TokenCount,
                Summary = m.Summary,
            };

            // Persist user attachments (images + text files including pasted text)
            if (m.Role == ChatRole.User && m.Attachments is { Count: > 0 })
            {
                foreach (var att in m.Attachments)
                {
                    if (att.Type is not (AttachmentType.Image
                                        or AttachmentType.TextFile
                                        or AttachmentType.Audio
                                        or AttachmentType.Document))
                        continue;

                    var mime = att.MimeType ?? "application/octet-stream";
                    var entryName = AddMediaEntry(mediaEntries, att.Data, mime);
                    saved.Media ??= [];

                    var source = att.Source == AttachmentSource.Pasted ? "user_pasted" : "user_upload";
                    var mediaRef = new MediaReference
                    {
                        Id = $"upload_{saved.Media.Count}",
                        FileName = entryName,
                        MimeType = mime,
                        Source = source,
                        OriginalFileName = att.FileName != entryName ? att.FileName : null,
                    };

                    if (att.Source == AttachmentSource.Pasted)
                    {
                        mediaRef.CharCount = att.CharCount;
                        mediaRef.LineCount = att.LineCount;
                    }

                    if (att.Type == AttachmentType.Audio && att.Duration is { } dur && dur > TimeSpan.Zero)
                    {
                        mediaRef.DurationSeconds = (long)dur.TotalSeconds;
                    }

                    saved.Media.Add(mediaRef);
                }
            }

            // Persist tool result media (Tool role) and assistant-generated inline media
            // (Assistant role, e.g. Gemini image-generation output).
            if (m.ToolMedia is { Count: > 0 } && m.Role is ChatRole.Tool or ChatRole.Assistant)
            {
                var source = m.Role == ChatRole.Tool ? "mcp_tool" : "assistant_generated";
                var idPrefix = m.Role == ChatRole.Tool ? "tool" : "assistant";
                foreach (var media in m.ToolMedia)
                {
                    var bytes = DecodeMediaUri(media.MediaUri);
                    if (bytes is null) continue;

                    var entryName = AddMediaEntry(mediaEntries, bytes, media.MimeType);
                    saved.Media ??= [];
                    saved.Media.Add(new MediaReference
                    {
                        Id = $"{idPrefix}_{saved.Media.Count}",
                        FileName = entryName,
                        MimeType = media.MimeType,
                        Source = source,
                        ToolCallId = m.Role == ChatRole.Tool ? m.ToolCallId : null
                    });
                }
            }

            savedMessages.Add(saved);
        }

        var data = new ConversationData
        {
            TabName = tabName,
            ProviderModelName = providerPresetName,
            ConversationId = conversationId,
            ActiveRootChildId = activeRootChildId,
            BuiltInServers = builtInServers,
            SavedAt = DateTime.UtcNow,
            Messages = savedMessages,
        };

        var manifest = new ManifestData
        {
            FormatVersion = CurrentFormatVersion,
            AppVersion = GetAppVersion(),
            CreatedAt = data.SavedAt,
            ModifiedAt = data.SavedAt,
            MediaCount = mediaEntries.Count,
            MessageCount = savedMessages.Count,
        };

        // Atomic write: create zip in temp file, then rename
        var dir = Path.GetDirectoryName(filePath) ?? ".";
        Directory.CreateDirectory(dir);
        var tempPath = filePath + ".tmp";
        try
        {
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                // Write manifest.json
                var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
                await using (var stream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(stream, manifest, ManifestTypeInfo);
                }

                // Write conversation.json
                var conversationEntry = archive.CreateEntry(ConversationEntryName, CompressionLevel.Optimal);
                await using (var stream = conversationEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(stream, data, ConversationTypeInfo);
                }

                // Write media entries (text gets compression; images are already compressed)
                foreach (var (entryName, (bytes, mimeType)) in mediaEntries)
                {
                    var level = mimeType.StartsWith("text/", StringComparison.Ordinal)
                        ? CompressionLevel.Optimal
                        : CompressionLevel.NoCompression;
                    var mediaEntry = archive.CreateEntry(entryName, level);
                    await using var stream = mediaEntry.Open();
                    await stream.WriteAsync(bytes);
                }
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            LoggingService.LogException(ex);
            try { File.Delete(tempPath); } catch { }
            throw;
        }

        LoggingService.LogInfo($"[File] Saved: {Path.GetFileName(filePath)}, messages={messages.Count}, media={mediaEntries.Count}, conversationId={conversationId}");
    }

    /// <summary>
    /// Loads a conversation from an .astx archive file.
    /// Returns the conversation data, all media bytes, and the manifest metadata.
    /// </summary>
    public static async Task<LoadConversationResult?> LoadConversationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            LoggingService.LogWarning($"[File] File not found: {filePath}");
            return null;
        }

        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read);

            // Read and validate manifest
            var manifestEntry = archive.GetEntry(ManifestEntryName)
                ?? throw new InvalidOperationException("Invalid .astx file: missing manifest.json.");

            ManifestData? manifest;
            using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync(stream, ManifestTypeInfo);
            }

            if (manifest is null)
                throw new InvalidOperationException("Failed to deserialize manifest.json.");

            if (manifest.FormatVersion != CurrentFormatVersion)
                throw new InvalidOperationException(
                    $"Unsupported .astx format version: {manifest.FormatVersion} (expected {CurrentFormatVersion}).");

            // Read conversation data
            var conversationEntry = archive.GetEntry(ConversationEntryName)
                ?? throw new InvalidOperationException("Invalid .astx file: missing conversation.json.");

            ConversationData? data;
            using (var stream = conversationEntry.Open())
            {
                data = await JsonSerializer.DeserializeAsync(stream, ConversationTypeInfo);
            }

            if (data is null)
                throw new InvalidOperationException("Failed to deserialize conversation.json.");

            // Read all media entries
            var media = new Dictionary<string, byte[]>();
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(MediaDirPrefix, StringComparison.Ordinal))
                    continue;

                // Path traversal defense
                if (entry.FullName.Contains("..", StringComparison.Ordinal))
                {
                    LoggingService.LogWarning($"[File] Skipping suspicious zip entry: {entry.FullName}");
                    continue;
                }

                using var stream = entry.Open();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                media[entry.FullName] = ms.ToArray();
            }

            LoggingService.LogInfo($"[File] Loaded: {Path.GetFileName(filePath)}, messages={data.Messages.Count}, media={media.Count}");

            return new LoadConversationResult
            {
                Conversation = data,
                Media = media,
                Manifest = manifest,
            };
        }
        catch (InvalidDataException ex)
        {
            LoggingService.LogError($"[File] Corrupted .astx file: {ex.Message}");
            throw new InvalidOperationException($"The file '{Path.GetFileName(filePath)}' is corrupted or not a valid .astx archive.", ex);
        }
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
    /// Deletes all saved conversation files from the conversations folder.
    /// </summary>
    public static void ClearAll()
    {
        EnsureInitialized();

        if (!Directory.Exists(_conversationsFolder)) return;

        var files = Directory.GetFiles(_conversationsFolder, "*" + FileExtension);

        foreach (var file in files)
        {
            try { File.Delete(file); }
            catch (Exception ex) { LoggingService.LogException(ex); }
        }
    }

    /// <summary>
    /// Schedules a debounced auto-save for the given conversation.
    /// Rapid-fire calls within <see cref="DebounceInterval"/> are coalesced.
    /// The actual save runs on a background thread to avoid blocking the UI.
    /// At most one save runs concurrently; one additional save is queued.
    /// </summary>
    public static void ScheduleAutoSave(
        string? filePath,
        string tabName,
        string? providerPresetName,
        IReadOnlyList<ChatMessage> messages,
        string? activeRootChildId,
        Dictionary<string, BuiltInServerConfig>? builtInServers,
        string? conversationId)
    {
        // Cancel any pending debounce timer
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        // Capture message list reference (not a snapshot of field values).
        // ChatMessage objects are shared references — SaveToFileAsync reads
        // ActiveChildId etc. at serialization time, picking up the latest values.
        var messagesCopy = messages.ToList();

        _pendingAutoSave = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceInterval, ct);
            }
            catch (OperationCanceledException)
            {
                return; // Debounce reset — a newer save will take over
            }

            await _saveLock.WaitAsync(CancellationToken.None);
            try
            {
                if (filePath is not null)
                    await SaveToFileAsync(filePath, tabName, providerPresetName, messagesCopy, activeRootChildId, builtInServers, conversationId);
                else
                    await SaveConversationAsync(tabName, providerPresetName, messagesCopy, activeRootChildId, builtInServers, conversationId);
            }
            catch (Exception ex)
            {
                LoggingService.LogException(ex);
            }
            finally
            {
                _saveLock.Release();
            }
        });
    }

    /// <summary>
    /// Cancels any pending auto-save. Call after an explicit Save/SaveAs
    /// to prevent a stale auto-save from overwriting the just-saved file.
    /// </summary>
    public static void CancelPendingAutoSave()
    {
        _debounceCts?.Cancel();
    }

    /// <summary>
    /// Waits for any in-progress or pending auto-save to complete.
    /// Call this on app shutdown to ensure data is flushed before exit.
    /// </summary>
    public static async Task FlushAutoSaveAsync()
    {
        // Cancel debounce timer so the pending save fires immediately
        _debounceCts?.Cancel();

        if (_pendingAutoSave is not null)
        {
            try { await _pendingAutoSave; }
            catch { /* already logged */ }
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Adds a media byte array to the entries dictionary with content-addressed naming.
    /// Returns the zip entry name (e.g., "media/a1b2c3d4e5f67890.png").
    /// Deduplicates identical content automatically.
    /// </summary>
    private static string AddMediaEntry(Dictionary<string, (byte[] Data, string MimeType)> entries, byte[] data, string mimeType)
    {
        var entryName = ComputeMediaEntryName(data, mimeType);

        if (entries.TryGetValue(entryName, out var existing))
        {
            // Hash collision check: same short hash but different content
            if (!existing.Data.AsSpan().SequenceEqual(data))
            {
                // Fallback to full SHA-256 hash
                var fullHash = SHA256.HashData(data);
                var fullHashHex = Convert.ToHexStringLower(fullHash);
                var ext = MimeToExtension(mimeType);
                entryName = $"{MediaDirPrefix}{fullHashHex}{ext}";

                // If still collides (practically impossible), just use it
                entries.TryAdd(entryName, (data, mimeType));
            }
            // else: same content, deduplication — no need to add again
        }
        else
        {
            entries[entryName] = (data, mimeType);
        }

        return entryName;
    }

    /// <summary>
    /// Computes a short content-addressed zip entry name for a media binary.
    /// Uses the first 16 hex chars (64 bits) of SHA-256.
    /// </summary>
    private static string ComputeMediaEntryName(byte[] data, string mimeType)
    {
        var hash = SHA256.HashData(data);
        var hashHex = Convert.ToHexStringLower(hash.AsSpan(0, 8));
        var ext = MimeToExtension(mimeType);
        return $"{MediaDirPrefix}{hashHex}{ext}";
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
        "text/plain" => ".txt",
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

    /// <summary>
    /// Gets the application version string from the package identity.
    /// </summary>
    private static string GetAppVersion()
    {
        try
        {
            var ver = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{ver.Major}.{ver.Minor}.{ver.Build}";
        }
        catch
        {
            return "0.0.0";
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
