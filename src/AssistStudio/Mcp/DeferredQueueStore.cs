using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistStudio.Mcp;

/// <summary>
/// Manages the persistent deferred indexing queue stored as JSON.
/// Entries are added when the user chooses "Start when AssistStudio closes"
/// in the Create/Settings KB dialog, and consumed by the orchestrator
/// process (<c>exec-queue</c>) spawned at shutdown.
/// </summary>
public static class DeferredQueueStore
{
    #region Fields

    private static readonly string _queueFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "Mcp.Rag", ".deferred-queue.json");

    #endregion

    #region Public Properties

    /// <summary>
    /// Full path to the deferred queue JSON file. Shared with the
    /// <c>exec-queue</c> CLI command in the RAG process.
    /// </summary>
    public static string QueueFilePath => _queueFilePath;

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads the queue from disk. Returns an empty queue if the file
    /// is missing or corrupt.
    /// </summary>
    public static DeferredQueue Load()
    {
        try
        {
            if (!File.Exists(_queueFilePath))
                return new DeferredQueue();

            var json = File.ReadAllText(_queueFilePath);
            return JsonSerializer.Deserialize(json, DeferredQueueJsonContext.Default.DeferredQueue)
                ?? new DeferredQueue();
        }
        catch
        {
            return new DeferredQueue();
        }
    }

    /// <summary>
    /// Adds an entry to the queue. Idempotent — duplicate <paramref name="kbId"/> is ignored.
    /// </summary>
    public static void Add(string kbId, bool isReindex = false, string? partialMode = null)
    {
        var queue = Load();
        if (queue.Entries.Any(e => e.KbId == kbId))
            return;

        queue.Entries.Add(new DeferredIndexEntry
        {
            KbId = kbId,
            ScheduledAt = DateTime.UtcNow.ToString("o"),
            IsReindex = isReindex,
            PartialMode = partialMode,
        });
        Save(queue);
    }

    /// <summary>
    /// Removes an entry by KB ID. Used by "Cancel scheduled" and "Index now" actions.
    /// </summary>
    public static void Remove(string kbId)
    {
        var queue = Load();
        var removed = queue.Entries.RemoveAll(e => e.KbId == kbId);
        if (removed > 0)
            Save(queue);
    }

    /// <summary>
    /// Returns <c>true</c> if any entries are waiting (not yet started or failed).
    /// </summary>
    public static bool HasEntries()
    {
        var queue = Load();
        return queue.Entries.Count > 0;
    }

    /// <summary>
    /// Returns all entries for UI display.
    /// </summary>
    public static IReadOnlyList<DeferredIndexEntry> List() =>
        Load().Entries.AsReadOnly();

    /// <summary>
    /// Checks whether a specific KB is in the deferred queue.
    /// </summary>
    public static bool Contains(string kbId) =>
        Load().Entries.Any(e => e.KbId == kbId);

    /// <summary>
    /// Retrieves the entry for a specific KB, or <c>null</c> if not queued.
    /// </summary>
    public static DeferredIndexEntry? Get(string kbId) =>
        Load().Entries.FirstOrDefault(e => e.KbId == kbId);

    #endregion

    #region Private Methods

    private static void Save(DeferredQueue queue)
    {
        var dir = Path.GetDirectoryName(_queueFilePath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(queue, DeferredQueueJsonContext.Default.DeferredQueue);
        File.WriteAllText(_queueFilePath, json);
    }

    #endregion
}

#region Models

/// <summary>
/// Root object for the deferred queue JSON file.
/// </summary>
public sealed class DeferredQueue
{
    /// <summary>Schema version for forward compatibility.</summary>
    public int Version { get; set; } = 1;

    /// <summary>PID lock held by the currently-running orchestrator, if any.</summary>
    public DeferredQueueLock? Lock { get; set; }

    /// <summary>Ordered list of KBs waiting for indexing.</summary>
    public List<DeferredIndexEntry> Entries { get; set; } = [];
}

/// <summary>
/// PID-based lock preventing concurrent orchestrator instances.
/// </summary>
public sealed class DeferredQueueLock
{
    /// <summary>Process ID of the running orchestrator.</summary>
    public int Pid { get; set; }

    /// <summary>ISO 8601 timestamp when the orchestrator started.</summary>
    public string StartedAt { get; set; } = "";
}

/// <summary>
/// A single KB entry in the deferred indexing queue.
/// </summary>
public sealed class DeferredIndexEntry
{
    /// <summary>Knowledge base identifier.</summary>
    public string KbId { get; set; } = "";

    /// <summary>ISO 8601 timestamp when the user scheduled this entry.</summary>
    public string ScheduledAt { get; set; } = "";

    /// <summary>Whether this is a re-index (true) or first index (false).</summary>
    public bool IsReindex { get; set; }

    /// <summary>
    /// Partial re-index mode: "contextualization", "embedding", or <c>null</c> for full.
    /// </summary>
    public string? PartialMode { get; set; }

    /// <summary>ISO 8601 timestamp set by the orchestrator when processing begins.</summary>
    public string? StartedAt { get; set; }

    /// <summary>Error message set by the orchestrator on failure.</summary>
    public string? LastError { get; set; }
}

#endregion

#region JSON Context

/// <summary>
/// Source-generated JSON serializer context for the deferred queue.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeferredQueue))]
internal sealed partial class DeferredQueueJsonContext : JsonSerializerContext;

#endregion
