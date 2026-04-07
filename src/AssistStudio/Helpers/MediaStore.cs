namespace AssistStudio.Helpers;

/// <summary>
/// Manages media file storage for conversation images and other media.
/// Files are stored under %LocalAppData%/FieldCure/AssistStudio/media/{conversationId}/.
/// </summary>
public static class MediaStore
{
    #region Fields

    /// <summary>The base directory for all conversation media.</summary>
    private static string _baseDir = "";

    #endregion

    #region Public Methods

    /// <summary>
    /// Initialize the media store with a base folder path.
    /// Must be called before any save/load operations.
    /// </summary>
    public static void Initialize(string baseFolderPath)
    {
        _baseDir = Path.Combine(baseFolderPath, "media");
    }

    /// <summary>
    /// Saves media bytes to the media directory for a conversation.
    /// Returns the generated file name.
    /// </summary>
    public static async Task<string> SaveAsync(
        string conversationId, string fileNameHint, byte[] data, CancellationToken ct = default)
    {
        EnsureInitialized();

        var dir = Path.Combine(_baseDir, conversationId);
        Directory.CreateDirectory(dir);

        var fileName = GenerateUniqueFileName(dir, fileNameHint);
        var filePath = Path.Combine(dir, fileName);

        await File.WriteAllBytesAsync(filePath, data, ct);
        return fileName;
    }

    /// <summary>
    /// Loads media bytes from the media directory.
    /// Returns null if file not found.
    /// </summary>
    public static async Task<byte[]?> LoadAsync(
        string conversationId, string fileName, CancellationToken ct = default)
    {
        EnsureInitialized();

        var filePath = Path.Combine(_baseDir, conversationId, fileName);
        if (!File.Exists(filePath)) return null;
        return await File.ReadAllBytesAsync(filePath, ct);
    }

    /// <summary>
    /// Deletes all media files for a conversation.
    /// Call when conversation is deleted.
    /// </summary>
    public static void DeleteConversation(string conversationId)
    {
        EnsureInitialized();

        var dir = Path.Combine(_baseDir, conversationId);
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception ex) { LoggingService.LogException(ex); }
        }
    }

    /// <summary>
    /// Deletes all media files across all conversations.
    /// </summary>
    public static void ClearAll()
    {
        EnsureInitialized();

        if (!Directory.Exists(_baseDir)) return;

        try { Directory.Delete(_baseDir, recursive: true); }
        catch (Exception ex) { LoggingService.LogException(ex); }
    }

    /// <summary>
    /// Returns total size of all media files across all conversations.
    /// </summary>
    public static long GetTotalSizeBytes()
    {
        EnsureInitialized();

        if (!Directory.Exists(_baseDir)) return 0;
        return new DirectoryInfo(_baseDir)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Sum(f => f.Length);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Generates a unique file name in the target directory.
    /// If the file already exists, appends a numeric suffix.
    /// </summary>
    private static string GenerateUniqueFileName(string directory, string hint)
    {
        var name = Path.GetFileNameWithoutExtension(hint);
        var ext = Path.GetExtension(hint);
        var candidate = $"{name}{ext}";

        if (!File.Exists(Path.Combine(directory, candidate)))
            return candidate;

        for (var i = 1; i < 1000; i++)
        {
            candidate = $"{name}_{i}{ext}";
            if (!File.Exists(Path.Combine(directory, candidate)))
                return candidate;
        }

        // Fallback: use GUID
        return $"{Guid.NewGuid():N}{ext}";
    }

    /// <summary>Throws if <see cref="Initialize"/> has not been called.</summary>
    private static void EnsureInitialized()
    {
        if (string.IsNullOrEmpty(_baseDir))
            throw new InvalidOperationException(
                "MediaStore.Initialize() must be called before use.");
    }

    #endregion
}
