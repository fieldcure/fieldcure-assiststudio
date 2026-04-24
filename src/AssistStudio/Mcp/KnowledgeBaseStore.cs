using FieldCure.AssistStudio.Core.Models;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// CRUD operations for knowledge bases stored under
/// <c>%LOCALAPPDATA%\FieldCure\Mcp.Rag\{kb-id}\</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Backlog (post-v1.0):</b>
/// </para>
/// <list type="bullet">
///   <item><description>
///     <b>Unify KB listing via MCP</b> — the folder-scan guards in
///     <see cref="ListAll"/> and <see cref="AnyExists"/> duplicate the
///     logic that already lives in <c>MultiKbContext.ListKbs</c> on the
///     RAG serve side (backup-folder rejection, id/folder match, etc.).
///     Any new guard added to the RAG repo has to be mirrored here by
///     hand. The right fix is to route listing through the MCP
///     <c>list_knowledge_bases</c> tool so the logic lives in one place,
///     but that is blocked on deciding how the Knowledge Bases page
///     handles "serve not yet started" (lazy launch vs. spinner).
///   </description></item>
///   <item><description>
///     <b>Naming</b> — unified to "Knowledge Base" / "지식베이스" across
///     UI labels, code identifiers, and resource keys.
///   </description></item>
/// </list>
/// </remarks>
public static class KnowledgeBaseStore
{
    #region Constants

    /// <summary>
    /// Base path for all knowledge base folders.
    /// </summary>
    public static readonly string BasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "Mcp.Rag");

    private const string ConfigFileName = "config.json";
    private const string DbFileName = "rag.db";

    #endregion

    #region Methods

    /// <summary>
    /// Lists all knowledge bases by scanning for <c>config.json</c> in subdirectories.
    /// Applies the same four guards as <c>MultiKbContext.ListKbs</c> on the
    /// RAG serve side — see the class-level remarks for the backlog note on
    /// unifying these.
    /// </summary>
    public static List<KnowledgeBase> ListAll()
    {
        var result = new List<KnowledgeBase>();

        if (!Directory.Exists(BasePath))
            return result;

        foreach (var dir in Directory.GetDirectories(BasePath))
        {
            var folderName = Path.GetFileName(dir);

            // Guard 1: skip prefix-marked folders (backups, tmp, hidden) silently.
            if (folderName.StartsWith('.') || folderName.StartsWith('_'))
                continue;

            // Guard 2: config.json must exist.
            var configPath = Path.Combine(dir, ConfigFileName);
            if (!File.Exists(configPath))
                continue;

            // Guard 3: config.json must parse.
            KnowledgeBase? kb;
            try
            {
                var json = File.ReadAllText(configPath);
                kb = JsonSerializer.Deserialize(json, KnowledgeBaseJsonContext.Default.KnowledgeBase);
            }
            catch
            {
                // Skip malformed config files (no logger available in static class).
                continue;
            }

            if (kb is null)
                continue;

            // Guard 4: folder name must match config.Id (case-insensitive).
            // Mismatches are typically copy/backup folders created outside the app
            // (e.g. "{kb-id}-backup-{timestamp}") that would otherwise show up as
            // phantom duplicates with the same display name.
            if (!string.Equals(folderName, kb.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add(kb);
        }

        // Stable case-insensitive alphabetical order by display name so the
        // Knowledge Bases page always lists KBs in a predictable order
        // regardless of filesystem enumeration order (which is undefined on
        // NTFS and varies between machines).
        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return result;
    }

    /// <summary>
    /// Creates a new knowledge base: generates UUID, creates folder, writes config.json.
    /// Returns the created <see cref="KnowledgeBase"/>.
    /// </summary>
    public static KnowledgeBase Create(
        string name,
        List<string> sourcePaths,
        KbProviderConfig embedding,
        KbProviderConfig contextualizer)
    {
        var kb = new KnowledgeBase
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Created = DateTime.UtcNow.ToString("O"),
            SourcePaths = sourcePaths,
            Embedding = embedding,
            Contextualizer = contextualizer,
        };

        var kbPath = GetKbPath(kb.Id);
        Directory.CreateDirectory(kbPath);

        var json = JsonSerializer.Serialize(kb, KnowledgeBaseJsonContext.Default.KnowledgeBase);
        File.WriteAllText(Path.Combine(kbPath, ConfigFileName), json);

        return kb;
    }

    /// <summary>
    /// Updates an existing knowledge base's config.json.
    /// </summary>
    public static void Update(KnowledgeBase kb)
    {
        var kbPath = GetKbPath(kb.Id);
        if (!Directory.Exists(kbPath))
            throw new DirectoryNotFoundException($"Knowledge base not found: {kb.Id}");

        var json = JsonSerializer.Serialize(kb, KnowledgeBaseJsonContext.Default.KnowledgeBase);
        File.WriteAllText(Path.Combine(kbPath, ConfigFileName), json);
    }

    /// <summary>
    /// Logically deletes a knowledge base by removing its config.json.
    /// The physical folder is left behind for <c>prune-orphans</c> to clean up
    /// at the next app startup (before serve acquires SQLite handles).
    /// Queue cleanup and cache eviction happen lazily on the serve side.
    /// </summary>
    public static void Delete(string kbId)
    {
        var configPath = Path.Combine(GetKbPath(kbId), ConfigFileName);
        if (File.Exists(configPath))
            File.Delete(configPath);
    }

    /// <summary>
    /// Returns the full path for a knowledge base folder.
    /// </summary>
    public static string GetKbPath(string kbId) =>
        Path.Combine(BasePath, kbId);

    /// <summary>
    /// Returns indexing statistics for a knowledge base by reading rag.db.
    /// Returns null if the database does not exist.
    /// </summary>
    public static KbStats? GetStats(string kbId)
    {
        var dbPath = Path.Combine(GetKbPath(kbId), DbFileName);
        if (!File.Exists(dbPath))
            return null;

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var totalFiles = ExecuteScalarInt(conn, "SELECT COUNT(*) FROM file_index");
            var totalChunks = ExecuteScalarInt(conn, "SELECT COUNT(*) FROM chunks");

            return new KbStats(totalFiles, totalChunks);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the current indexing status by reading the <c>_indexing_lock</c> table.
    /// Returns null if not indexing or if the database does not exist.
    /// </summary>
    public static KbIndexingStatus? GetIndexingStatus(string kbId)
    {
        var dbPath = Path.Combine(GetKbPath(kbId), DbFileName);
        if (!File.Exists(dbPath))
            return null;

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString();

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pid, current, total FROM _indexing_lock WHERE id = 1";
            using var reader = cmd.ExecuteReader();

            if (!reader.Read())
                return null;

            var pid = reader.GetInt32(0);
            var current = reader.GetInt32(1);
            var total = reader.GetInt32(2);

            // Check if process is still alive
            try
            {
                System.Diagnostics.Process.GetProcessById(pid);
            }
            catch
            {
                // Process is dead — stale lock
                return null;
            }

            return new KbIndexingStatus(pid, current, total);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if any knowledge bases exist. Applies the same guards as
    /// <see cref="ListAll"/> so a stray backup folder does not mask the
    /// "no knowledge bases yet" onboarding state.
    /// </summary>
    public static bool AnyExists()
    {
        if (!Directory.Exists(BasePath))
            return false;

        foreach (var dir in Directory.GetDirectories(BasePath))
        {
            var folderName = Path.GetFileName(dir);

            if (folderName.StartsWith('.') || folderName.StartsWith('_'))
                continue;

            var configPath = Path.Combine(dir, ConfigFileName);
            if (!File.Exists(configPath))
                continue;

            KnowledgeBase? kb;
            try
            {
                var json = File.ReadAllText(configPath);
                kb = JsonSerializer.Deserialize(json, KnowledgeBaseJsonContext.Default.KnowledgeBase);
            }
            catch
            {
                continue;
            }

            if (kb is null)
                continue;

            if (!string.Equals(folderName, kb.Id, StringComparison.OrdinalIgnoreCase))
                continue;

            return true;
        }

        return false;
    }

    #endregion

    #region Private Helpers

    private static int ExecuteScalarInt(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    #endregion
}

/// <summary>
/// Indexing statistics for a knowledge base.
/// </summary>
public sealed record KbStats(int TotalFiles, int TotalChunks);

/// <summary>
/// Current indexing status when exec is running.
/// </summary>
public sealed record KbIndexingStatus(int Pid, int Current, int Total);
