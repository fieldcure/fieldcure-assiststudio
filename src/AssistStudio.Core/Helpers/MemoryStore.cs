using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// A single memory entry stored in the persistent memory file.
/// </summary>
public sealed class MemoryEntry
{
    /// <summary>Unique identifier (e.g., "mem_a1b2c3d4e5f6").</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>The remembered content as a concise third-person statement.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>When this entry was created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>How the entry was created: "explicit" (user asked) or "auto" (future).</summary>
    [JsonPropertyName("source")]
    public string Source { get; init; } = "explicit";
}

/// <summary>
/// Root object for the memory JSON file.
/// </summary>
internal sealed class MemoryData
{
    [JsonPropertyName("entries")]
    public List<MemoryEntry> Entries { get; set; } = [];
}

/// <summary>
/// Manages persistent memory entries stored in a local JSON file.
/// Memory entries are automatically injected into the system prompt for every conversation.
/// </summary>
public sealed class MemoryStore
{
    #region Fields

    private string _filePath = "";
    private MemoryData _data = new();
    private readonly object _lock = new();

    #endregion

    #region Constants

    /// <summary>Soft limit for memory entries. Entries beyond this trigger warnings.</summary>
    public const int MaxEntries = 50;

    /// <summary>Warning threshold — entries at or above this count get a warning.</summary>
    private const int WarningThreshold = 45;

    #endregion

    #region Events

    /// <summary>
    /// Raised when memory entries are added, removed, or cleared.
    /// </summary>
    public event Action? Changed;

    #endregion

    #region Properties

    /// <summary>Gets the current number of memory entries.</summary>
    public int Count
    {
        get { lock (_lock) return _data.Entries.Count; }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes the memory store with the given file path and loads existing entries.
    /// </summary>
    /// <param name="filePath">Full path to the memory JSON file.</param>
    public void Initialize(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    /// <summary>
    /// Adds a new memory entry. Returns success status and an optional warning message
    /// when the store is approaching or exceeding the soft limit.
    /// </summary>
    public (bool Success, string? Warning) Add(string content, string source = "explicit")
    {
        if (string.IsNullOrWhiteSpace(content))
            return (false, null);

        lock (_lock)
        {
            _data.Entries.Add(new MemoryEntry
            {
                Id = $"mem_{Guid.NewGuid():N}"[..16],
                Content = content.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
                Source = source
            });
            Save();
        }

        Changed?.Invoke();

        string? warning = null;
        var count = Count;
        if (count >= MaxEntries)
        {
            warning = $"Memory has {count}/{MaxEntries} entries and has exceeded the soft limit. "
                + "Consider removing old entries to keep memory concise.";
        }
        else if (count >= WarningThreshold)
        {
            warning = $"Memory has {count}/{MaxEntries} entries. "
                + "Consider removing old entries to make room.";
        }

        return (true, warning);
    }

    /// <summary>
    /// Removes the first entry whose content matches the query (case-insensitive substring match).
    /// </summary>
    public bool RemoveByQuery(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        lock (_lock)
        {
            var entry = _data.Entries.FirstOrDefault(e =>
                e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return false;

            _data.Entries.Remove(entry);
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Removes an entry by its unique ID.
    /// </summary>
    public bool RemoveById(string id)
    {
        lock (_lock)
        {
            var entry = _data.Entries.FirstOrDefault(e => e.Id == id);
            if (entry is null) return false;

            _data.Entries.Remove(entry);
            Save();
        }

        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// Returns all memory entries as a read-only list.
    /// </summary>
    public IReadOnlyList<MemoryEntry> GetAll()
    {
        lock (_lock)
            return _data.Entries.ToList().AsReadOnly();
    }

    /// <summary>
    /// Removes all memory entries.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _data.Entries.Clear();
            Save();
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Builds a formatted text block for injection into the system prompt via PromptBuilder.
    /// Returns <c>null</c> if there are no entries.
    /// </summary>
    public string? BuildMemoryText()
    {
        var entries = GetAll();
        if (entries.Count == 0)
            return null;

        var sb = new StringBuilder();
        sb.AppendLine("## User Memory");
        sb.AppendLine("The following information has been saved from previous conversations:");
        foreach (var entry in entries)
        {
            sb.AppendLine($"- {entry.Content}");
        }

        return sb.ToString().TrimEnd();
    }

    #endregion

    #region Private Methods

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _data = JsonSerializer.Deserialize(json, MemoryJsonContext.Default.MemoryData)
                    ?? new MemoryData();
            }
        }
        catch
        {
            _data = new MemoryData();
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_data, MemoryJsonContext.Default.MemoryData);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Silently fail — memory is best-effort persistence
        }
    }

    #endregion
}

/// <summary>
/// Source-generated JSON context for memory serialization (trim-safe).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MemoryData))]
internal partial class MemoryJsonContext : JsonSerializerContext
{
}
