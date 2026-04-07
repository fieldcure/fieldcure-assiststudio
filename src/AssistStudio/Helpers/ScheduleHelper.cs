using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Windows.ApplicationModel.Resources;

namespace AssistStudio.Helpers;

/// <summary>
/// Data model representing a scheduled task stored in the Runner's SQLite database.
/// </summary>
public record ScheduleItem(
    string Id,
    string Name,
    string? Description,
    string? Schedule,
    DateTimeOffset? ScheduleOnce,
    bool IsEnabled,
    string? OutputChannel,
    DateTimeOffset CreatedAt);

/// <summary>
/// Static helper for querying and managing Runner scheduled tasks.
/// Reads from the Runner's SQLite database and synchronizes changes
/// with Windows Task Scheduler entries.
/// </summary>
public static class ScheduleHelper
{
    /// <summary>
    /// schtasks task name prefix used by the Runner's <c>SchedulerService</c>.
    /// </summary>
    private const string SchtasksPrefix = "AssistStudio_Runner_";

    /// <summary>
    /// Path to the Runner's SQLite database.
    /// </summary>
    private static readonly string DbPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "AssistStudio", "Runner", "runner.db");

    /// <summary>
    /// Path to the Runner's data directory (for log file cleanup).
    /// </summary>
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "AssistStudio", "Runner");

    /// <summary>
    /// Returns whether the Runner database file exists.
    /// </summary>
    public static bool DatabaseExists() => File.Exists(DbPath);

    /// <summary>
    /// Lists all scheduled tasks from the Runner database.
    /// Returns an empty list if the database does not exist.
    /// </summary>
    public static async Task<List<ScheduleItem>> ListAsync()
    {
        if (!DatabaseExists())
            return [];

        var items = new List<ScheduleItem>();

        await using var conn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT Id, Name, Description, Schedule, ScheduleOnce, IsEnabled, OutputChannel, CreatedAt
            FROM Tasks
            ORDER BY CreatedAt DESC
            """;

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new ScheduleItem(
                Id: reader.GetString(0),
                Name: reader.GetString(1),
                Description: reader.IsDBNull(2) ? null : reader.GetString(2),
                Schedule: reader.IsDBNull(3) ? null : reader.GetString(3),
                ScheduleOnce: !reader.IsDBNull(4) && DateTimeOffset.TryParse(reader.GetString(4), out var once)
                    ? once : null,
                IsEnabled: reader.GetInt32(5) != 0,
                OutputChannel: reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt: DateTimeOffset.TryParse(reader.GetString(7), out var dt)
                    ? dt : DateTimeOffset.MinValue));
        }

        return items;
    }

    /// <summary>
    /// Deletes a task: unregisters from schtasks, removes DB row + executions, and deletes log files.
    /// Mirrors the Runner's <c>DeleteTaskTool</c> logic.
    /// </summary>
    public static async Task<(bool Success, string? Error)> DeleteAsync(string taskId)
    {
        if (!DatabaseExists())
            return (false, "Runner database not found.");

        try
        {
            // 1. Unregister from Windows Task Scheduler (best-effort)
            await RunSchtasksAsync($"/DELETE /TN \"{SchtasksPrefix}{taskId}\" /F");

            // 2. Collect log paths before cascade delete
            var logPaths = new List<string>();
            await using (var readConn = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly"))
            {
                await readConn.OpenAsync();
                await using var logCmd = readConn.CreateCommand();
                logCmd.CommandText = "SELECT LogPath FROM TaskExecutions WHERE TaskId = @id AND LogPath IS NOT NULL";
                logCmd.Parameters.AddWithValue("@id", taskId);
                await using var reader = await logCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    logPaths.Add(reader.GetString(0));
            }

            // 3. Delete from DB (executions cascade via separate DELETE since no FK cascade)
            await using (var conn = new SqliteConnection($"Data Source={DbPath}"))
            {
                await conn.OpenAsync();

                await using var delExec = conn.CreateCommand();
                delExec.CommandText = "DELETE FROM TaskExecutions WHERE TaskId = @id";
                delExec.Parameters.AddWithValue("@id", taskId);
                await delExec.ExecuteNonQueryAsync();

                await using var delTask = conn.CreateCommand();
                delTask.CommandText = "DELETE FROM Tasks WHERE Id = @id";
                delTask.Parameters.AddWithValue("@id", taskId);
                var rows = await delTask.ExecuteNonQueryAsync();

                if (rows == 0)
                    return (false, "Task not found in database.");
            }

            // 4. Delete log files (best-effort)
            foreach (var logPath in logPaths)
            {
                var fullPath = Path.Combine(DataDir, logPath);
                try { if (File.Exists(fullPath)) File.Delete(fullPath); }
                catch { /* best-effort */ }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Enables or disables a task: updates the DB <c>IsEnabled</c> column
    /// and synchronizes the schtasks entry.
    /// </summary>
    public static async Task<(bool Success, string? Error)> SetEnabledAsync(string taskId, bool enable)
    {
        if (!DatabaseExists())
            return (false, "Runner database not found.");

        try
        {
            // 1. Update DB
            await using var conn = new SqliteConnection($"Data Source={DbPath}");
            await conn.OpenAsync();

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Tasks SET IsEnabled = @enabled, UpdatedAt = @now
                WHERE Id = @id
                """;
            cmd.Parameters.AddWithValue("@enabled", enable ? 1 : 0);
            cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@id", taskId);
            var rows = await cmd.ExecuteNonQueryAsync();

            if (rows == 0)
                return (false, "Task not found in database.");

            // 2. Sync schtasks (best-effort)
            var flag = enable ? "/ENABLE" : "/DISABLE";
            await RunSchtasksAsync($"/CHANGE /TN \"{SchtasksPrefix}{taskId}\" {flag}");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Converts a 5-field cron expression to a human-readable description
    /// using the current UI culture (e.g. "Every day at 06:00 AM" or "매일 오전 06:00에").
    /// Returns the raw cron string if conversion fails.
    /// </summary>
    /// <summary>
    /// Cached resource resolver that loads <c>Cron_</c>-prefixed strings via <see cref="ResourceLoader"/>.
    /// Returns <c>null</c> for missing keys so <see cref="Cron.ExpressionDescriptor"/> falls back to English.
    /// Returns <c>""</c> for intentionally empty keys (e.g. ko-KR <c>Cron_At</c>).
    /// </summary>
    private static Func<string, string?>? s_cronResolver;

    public static string DescribeCron(string? cron)
    {
        if (string.IsNullOrWhiteSpace(cron))
            return "";

        try
        {
            s_cronResolver ??= BuildCronResolver();

            var lang = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            var isKorean = lang == "ko";
            var desc = Cron.ExpressionDescriptor.GetDescription(cron, new Cron.Options
            {
                Use24HourTimeFormat = true,
                TimeAfterDescription = isKorean,
                StringResolver = s_cronResolver,
            });

            return isKorean ? Cron.KoreanPostProcessor.Process(desc) : desc;
        }
        catch
        {
            return cron;
        }
    }

    /// <summary>
    /// Builds a resolver that pre-loads all <c>Cron_</c> resource strings into a dictionary.
    /// This avoids repeated <see cref="ResourceLoader"/> calls and correctly preserves empty values.
    /// </summary>
    private static Func<string, string?> BuildCronResolver()
    {
        var cache = new Dictionary<string, string>();

        try
        {
            var loader = new ResourceLoader();
            string[] keys =
            [
                "EveryMinute", "EveryHour", "EverySecond",
                "AnErrorOccurredWhenGeneratingTheExpressionD",
                "At", "AtSpace", "AtX0",
                "AtX0MinutesPastTheHour", "AtX0SecondsPastTheMinute",
                "BetweenX0AndX1", "EveryMinuteBetweenX0AndX1",
                "EveryX0Seconds", "EveryX0Minutes", "EveryX0Hours",
                "SecondsX0ThroughX1PastTheMinute", "MinutesX0ThroughX1PastTheHour",
                "ComaEveryDay", "ComaEveryMinute", "ComaEveryHour",
                "ComaEveryX0Days", "ComaEveryX0DaysOfTheWeek",
                "ComaEveryX0Months", "ComaEveryX0Years",
                "ComaOnDayX0OfTheMonth", "ComaOnThe",
                "ComaOnTheX0OfTheMonth", "ComaOnTheLastDayOfTheMonth",
                "ComaOnTheLastWeekdayOfTheMonth", "ComaOnTheLastX0OfTheMonth",
                "ComaBetweenDayX0AndX1OfTheMonth", "CommaDaysBeforeTheLastDayOfTheMonth",
                "ComaOnlyOnX0", "ComaOnlyInX0", "ComaOnlyInYearX0",
                "ComaX0ThroughX1", "CommaStartingX0",
                "First", "Second", "Third", "Fourth", "Fifth",
                "FirstWeekday", "WeekdayNearestDayX0",
                "SpaceX0OfTheMonth", "SpaceAnd", "SpaceAndSpace",
                "AMPeriod", "PMPeriod",
            ];

            foreach (var key in keys)
            {
                // ResourceLoader.GetString returns "" for both missing and intentionally empty keys.
                // Since all keys above are defined in resw, "" here means intentionally empty.
                cache[key] = loader.GetString($"Cron_{key}");
            }
        }
        catch { /* ResourceLoader unavailable — cache stays empty, fallback to English */ }

        return name => cache.TryGetValue(name, out var value) ? value : null;
    }

    #region Private Helpers

    /// <summary>
    /// Runs schtasks.exe with the given arguments. Returns exit code and output.
    /// </summary>
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunSchtasksAsync(string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
                return (-1, "", "Failed to start schtasks process");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    #endregion
}
