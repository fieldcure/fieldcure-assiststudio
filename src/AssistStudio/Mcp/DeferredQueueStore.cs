using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistStudio.Mcp;

/// <summary>
/// Read-only accessor for the deferred indexing queue file path.
/// The queue is fully managed by the RAG serve process — AssistStudio
/// never writes to it. This class provides:
/// <list type="bullet">
///   <item><see cref="QueueFilePath"/> — for spawning the orchestrator CLI</item>
///   <item><see cref="HasPendingEntries"/> — fallback guard for shutdown dialog</item>
/// </list>
/// </summary>
public static class DeferredQueueStore
{
    private static readonly string _queueFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "Mcp.Rag", ".deferred-queue.json");

    /// <summary>
    /// Full path to the deferred queue JSON file. Shared with the
    /// <c>exec-queue</c> CLI command in the RAG process.
    /// </summary>
    public static string QueueFilePath => _queueFilePath;

    /// <summary>
    /// Returns <c>true</c> if the queue file exists and has entries
    /// that haven't been started or failed. Used as a fallback guard
    /// in the shutdown dialog (primary check is <c>DeferredThisSession</c>).
    /// </summary>
    public static bool HasPendingEntries()
    {
        try
        {
            if (!File.Exists(_queueFilePath))
                return false;

            var json = File.ReadAllText(_queueFilePath);
            var queue = JsonSerializer.Deserialize(json, DeferredQueueJsonContext.Default.DeferredQueue);
            return queue?.Entries.Any(e => e.StartedAt is null && e.LastError is null) == true;
        }
        catch
        {
            return false;
        }
    }
}

#region Models

public sealed class DeferredQueue
{
    public int Version { get; set; } = 1;
    public List<DeferredIndexEntry> Entries { get; set; } = [];
}

public sealed class DeferredIndexEntry
{
    public string KbId { get; set; } = "";
    public string ScheduledAt { get; set; } = "";
    public bool IsReindex { get; set; }
    public string? PartialMode { get; set; }
    public bool Force { get; set; }
    public bool Deferred { get; set; }
    public string? StartedAt { get; set; }
    public string? LastError { get; set; }
}

#endregion

#region JSON Context

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DeferredQueue))]
internal sealed partial class DeferredQueueJsonContext : JsonSerializerContext;

#endregion
