using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace AnthropicSdkSample.Helpers;

/// <summary>
/// Provides centralized logging with asynchronous file writing, daily rotation, and retention management.
/// </summary>
public static class LoggingService
{
    #region Constants

    private const string MessageFormatString = "{0} [{1}] {2}";
    private const int MaxFileSizeMB = 10;
    private const int MaxLogFiles = 30;
    private const int MaxAgeInDays = 30;
    private const LogLevel MinimumFileLogLevel =
#if DEBUG
        LogLevel.Debug;
#else
        LogLevel.Info;
#endif

    #endregion

    #region Fields

    private static readonly ConcurrentQueue<string> MessageQueue = new();
    private static readonly SemaphoreSlim SemaphoreSlim = new(1, 1);
    private static readonly List<string> Messages = [];

    private static string? logFilePath;
    private static string? logsFolderPath;
    private static CancellationTokenSource? cancellationTokenSource;
    private static Task? backgroundTask;
    private static bool initialized;

    #endregion

    #region Enums

    private enum LogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Initializes file system logging and starts the background writer.
    /// </summary>
    /// <param name="baseFolderPath">The base application data folder path.</param>
    public static async Task InitializeAsync(string baseFolderPath)
    {
        if (initialized)
        {
            return;
        }

        logsFolderPath = Path.Combine(baseFolderPath, "Logs");
        Directory.CreateDirectory(logsFolderPath);

        CleanupOldLogs();
        await InitializeLogFileWriterBackgroundTaskAsync();
    }

    /// <summary>
    /// Gets the current log file path.
    /// </summary>
    public static string? GetLogFilePath() => logFilePath;

    /// <summary>
    /// Gets the logs folder path.
    /// </summary>
    public static string? GetLogsFolderPath() => logsFolderPath;

    /// <summary>
    /// Logs an informational message.
    /// </summary>
    public static void LogInfo(string message, bool consoleOnly = false)
        => LogMessage(LogLevel.Info, message, consoleOnly);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    public static void LogWarning(string message, bool consoleOnly = false)
        => LogMessage(LogLevel.Warning, message, consoleOnly);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    public static void LogError(string message, bool consoleOnly = false)
        => LogMessage(LogLevel.Error, message, consoleOnly);

    /// <summary>
    /// Logs an exception with full stack trace.
    /// </summary>
    public static void LogException(Exception ex, bool consoleOnly = false)
    {
        if (ex == null) { return; }
        LogError(ex.ToString(), consoleOnly);
    }

    /// <summary>
    /// Flushes the queued log messages to the file.
    /// </summary>
    public static async Task<bool> TryFlushMessageQueueAsync()
    {
        if (!initialized || logFilePath == null)
        {
            return false;
        }

        await SemaphoreSlim.WaitAsync();

        try
        {
            if (MessageQueue.IsEmpty)
            {
                return true;
            }

            while (MessageQueue.TryDequeue(out var message))
            {
                Messages.Add(message);
            }

            await File.AppendAllLinesAsync(logFilePath, Messages);
            Messages.Clear();
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            SemaphoreSlim.Release();
        }

        return false;
    }

    /// <summary>
    /// Shuts down the logging service gracefully.
    /// </summary>
    public static async Task ShutdownAsync()
    {
        try
        {
            cancellationTokenSource?.Cancel();

            if (backgroundTask != null)
            {
                await backgroundTask.WaitAsync(TimeSpan.FromSeconds(5));
            }

            await TryFlushMessageQueueAsync();
            CleanupOldLogs();

            initialized = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error during logging shutdown: {ex.Message}");
        }
        finally
        {
            cancellationTokenSource?.Dispose();
            cancellationTokenSource = null;
            backgroundTask = null;
            logFilePath = null;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Formats and enqueues a log message.
    /// </summary>
    private static void LogMessage(LogLevel level, string message, bool consoleOnly)
    {
        var timeStamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var levelStr = level switch
        {
            LogLevel.Debug => "Debug",
            LogLevel.Info => "Info",
            LogLevel.Warning => "Warning",
            LogLevel.Error => "Error",
            _ => "Unknown"
        };
        var formattedMessage = string.Format(MessageFormatString, timeStamp, levelStr, message);

        Debug.WriteLine(formattedMessage);

        if (!initialized || consoleOnly)
        {
            return;
        }

        if (level < MinimumFileLogLevel)
        {
            return;
        }

        MessageQueue.Enqueue(formattedMessage);
    }

    /// <summary>
    /// Gets or creates today's log file path, rotating if size limit is exceeded.
    /// </summary>
    private static string? GetOrCreateTodayLogFilePath()
    {
        try
        {
            if (logsFolderPath == null) return null;

            var fileName = $"{DateTime.Now:yyyyMMdd}.log";
            var filePath = Path.Combine(logsFolderPath, fileName);

            if (File.Exists(filePath))
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSizeMB * 1024L * 1024L)
                {
                    RotateLogFile(filePath);
                }
            }

            return filePath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to get/create log file: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Rotates a log file that has exceeded size limits.
    /// </summary>
    private static void RotateLogFile(string filePath)
    {
        try
        {
            if (logsFolderPath == null) return;

            var baseName = Path.GetFileNameWithoutExtension(filePath);
            var sequence = 1;

            while (File.Exists(Path.Combine(logsFolderPath, $"{baseName}_{sequence:D3}.log")))
            {
                sequence++;
            }

            File.Move(filePath, Path.Combine(logsFolderPath, $"{baseName}_{sequence:D3}.log"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to rotate log file: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up old log files based on retention settings.
    /// </summary>
    private static void CleanupOldLogs()
    {
        try
        {
            if (logsFolderPath == null || !Directory.Exists(logsFolderPath)) return;

            var logFiles = Directory.GetFiles(logsFolderPath, "*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.LastWriteTime)
                .ToList();

            if (logFiles.Count == 0) return;

            var now = DateTime.Now;
            var fileCount = 0;

            foreach (var file in logFiles)
            {
                fileCount++;

                var exceedsCount = fileCount > MaxLogFiles;
                var tooOld = (now - file.LastWriteTime).TotalDays > MaxAgeInDays;

                if (exceedsCount || tooOld)
                {
                    try
                    {
                        file.Delete();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete log file {file.Name}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to cleanup old logs: {ex.Message}");
        }
    }

    /// <summary>
    /// Initializes the background task that writes log messages to file.
    /// </summary>
    private static async Task<bool> InitializeLogFileWriterBackgroundTaskAsync()
    {
        await SemaphoreSlim.WaitAsync();

        if (backgroundTask != null && !backgroundTask.IsCompleted)
        {
            SemaphoreSlim.Release();
            return false;
        }

        try
        {
            logFilePath = GetOrCreateTodayLogFilePath();
            if (logFilePath == null)
            {
                return false;
            }

            cancellationTokenSource = new CancellationTokenSource();
            backgroundTask = Task.Run(() => WriteLogMessagesAsync(cancellationTokenSource.Token));

            initialized = true;
            LogInfo($"Log file location: {logFilePath}", true);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            SemaphoreSlim.Release();
        }

        return false;
    }

    /// <summary>
    /// Background loop that periodically writes messages from the queue to disk.
    /// </summary>
    private static async Task WriteLogMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);

                // Check if date has changed
                var expectedPrefix = DateTime.Now.ToString("yyyyMMdd");
                if (logFilePath != null && !Path.GetFileName(logFilePath).StartsWith(expectedPrefix))
                {
                    logFilePath = GetOrCreateTodayLogFilePath();
                }

                if (!await TryFlushMessageQueueAsync() && Messages.Count > 1000)
                {
                    Messages.Clear();
                    while (MessageQueue.TryDequeue(out _)) { }
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in log writer: {ex.Message}");
            }
        }

        await TryFlushMessageQueueAsync();
    }

    #endregion
}
