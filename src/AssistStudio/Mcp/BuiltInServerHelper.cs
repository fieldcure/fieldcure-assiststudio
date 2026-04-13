using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssistStudio.Mcp;

/// <summary>
/// Static helper for built-in MCP server configuration resolution and
/// <see cref="McpServerConfig"/> construction.
/// </summary>
public static class BuiltInServerHelper
{
    #region Constants

    /// <summary>
    /// Base installation path for built-in server dotnet tools.
    /// </summary>
    private static readonly string ToolInstallPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FieldCure", "AssistStudio", "tools");

    /// <summary>
    /// Maps server keys to their NuGet package IDs.
    /// Versions are resolved automatically via <c>dotnet tool install/update</c> (always latest stable).
    /// </summary>
    private static readonly Dictionary<string, string> NuGetPackages = new()
    {
        [FilesystemKey] = "FieldCure.Mcp.Filesystem",
        [RagKey] = "FieldCure.Mcp.Rag",
        [OutboxKey] = "FieldCure.Mcp.Outbox",
        [RunnerKey] = "FieldCure.AssistStudio.Runner",
        [EssentialsKey] = "FieldCure.Mcp.Essentials",
    };

    /// <summary>NuGet package ID for the Filesystem server.</summary>
    public const string FilesystemPackageId = "FieldCure.Mcp.Filesystem";

    /// <summary>Config key for the Essentials virtual server (no process, in-process tools).</summary>
    public const string EssentialsKey = "essentials";

    /// <summary>Display name for the Essentials virtual server.</summary>
    public const string EssentialsDisplayName = "Essentials";

    /// <summary>Config key for the Memory virtual server.</summary>
    public const string MemoryKey = "memory";

    /// <summary>Display name for the Memory virtual server.</summary>
    public const string MemoryDisplayName = "Memory";

    /// <summary>Config dictionary key for the Filesystem server.</summary>
    public const string FilesystemKey = "filesystem";

    /// <summary>Config dictionary key for the RAG server.</summary>
    public const string RagKey = "rag";

    /// <summary>Display name for the Filesystem server.</summary>
    public const string FilesystemDisplayName = "Workspace Folders";

    /// <summary>Display name for the RAG server.</summary>
    public const string RagDisplayName = "Knowledge Archive";

    /// <summary>Config dictionary key for the Outbox server.</summary>
    public const string OutboxKey = "outbox";

    /// <summary>Display name for the Outbox server.</summary>
    public const string OutboxDisplayName = "Outbox";

    /// <summary>Config dictionary key for the Runner server.</summary>
    public const string RunnerKey = "runner";

    /// <summary>Display name for the Runner server.</summary>
    public const string RunnerDisplayName = "Runner";

    /// <summary>
    /// Tool names from the built-in Filesystem MCP server that are read-only
    /// and do not require user confirmation.
    /// </summary>
    private static readonly HashSet<string> ReadOnlyToolNames =
    [
        "read_file", "read_multiple_files", "read_file_lines",
        "list_directory", "directory_tree",
        "search_files", "search_within_files", "get_file_info",
        // RAG
        "search_documents", "get_document_chunk", "get_index_info", "check_changes",
        // Outbox — add_channel opens a subprocess console for credential input
        "list_channels", "add_channel",
        // Runner
        "list_tasks", "get_execution_status", "get_task_history",
        // Essentials
        "get_environment", "run_javascript", "http_request",
        "web_search", "web_fetch",
        "remember", "forget", "list_memories",
    ];

    /// <summary>
    /// Legacy Memory tool names — now provided by Essentials MCP server.
    /// Kept for profile migration compatibility.
    /// </summary>
    [Obsolete("Memory tools are now part of Essentials MCP server.")]
    public static readonly HashSet<string> MemoryToolNames = ["remember", "forget"];

    /// <summary>
    /// Maps server keys to their executable names.
    /// </summary>
    private static readonly Dictionary<string, (string ExeName, string DisplayName)> ServerDefinitions = new()
    {
        [FilesystemKey] = ("fieldcure-mcp-filesystem", FilesystemDisplayName),
        [RagKey] = ("fieldcure-mcp-rag", RagDisplayName),
        [OutboxKey] = ("fieldcure-mcp-outbox", OutboxDisplayName),
        [RunnerKey] = ("assiststudio-runner", RunnerDisplayName),
        [EssentialsKey] = ("fieldcure-mcp-essentials", EssentialsDisplayName),
    };

    /// <summary>Path to the pending updates file.</summary>
    private static readonly string PendingUpdatesPath = Path.Combine(ToolInstallPath, "pending-updates.json");

    /// <summary>Static HttpClient for NuGet API queries with 10-second timeout.</summary>
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    #endregion

    #region Methods

    /// <summary>
    /// Returns the default built-in server configurations.
    /// Folder-based servers start disabled; shared servers (Outbox) start enabled.
    /// </summary>
    public static Dictionary<string, BuiltInServerConfig> GetDefaults() => new()
    {
        [FilesystemKey] = new BuiltInServerConfig { IsEnabled = false, Folders = [] },
        [RagKey] = new BuiltInServerConfig { IsEnabled = false, Folders = [] },
        [OutboxKey] = new BuiltInServerConfig { IsEnabled = true, Folders = [] },
        [RunnerKey] = new BuiltInServerConfig { IsEnabled = true, Folders = [] },
        [EssentialsKey] = new BuiltInServerConfig { IsEnabled = true, Folders = [] },
    };

    /// <summary>
    /// Merges App Settings defaults with optional per-conversation overrides.
    /// If the conversation has overrides, they take precedence entirely.
    /// </summary>
    public static Dictionary<string, BuiltInServerConfig> ResolveConfigs(
        Dictionary<string, BuiltInServerConfig> appDefaults,
        Dictionary<string, BuiltInServerConfig>? conversationConfigs = null)
    {
        if (conversationConfigs is { Count: > 0 })
            return new(conversationConfigs);

        return new(appDefaults);
    }

    /// <summary>
    /// Creates a <see cref="McpServerConfig"/> for a built-in server.
    /// Returns <see langword="null"/> if the server is disabled, has no folders
    /// (for folder-based servers), or the executable is not found.
    /// </summary>
    public static McpServerConfig? CreateMcpServerConfig(string serverKey, BuiltInServerConfig config)
    {
        if (!config.IsEnabled)
            return null;

        // Folder-based servers (Filesystem) require at least one folder
        if (!IsSharedServer(serverKey) && config.Folders.Count == 0)
            return null;

        // RAG shared server requires at least one KB to exist
        if (serverKey == RagKey && !KnowledgeBaseStore.AnyExists())
            return null;

        if (!ServerDefinitions.TryGetValue(serverKey, out var def))
            return null;

        var exePath = GetServerExePath(serverKey);
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            return null;

        var mcpConfig = new McpServerConfig
        {
            Id = $"builtin_{serverKey}",
            Name = def.DisplayName,
            TransportType = McpTransportType.Stdio,
            Command = exePath,
            Arguments = config.Folders.Count > 0 ? [.. config.Folders] : [],
            IsEnabled = true,
            IsBuiltIn = true,
            Description = serverKey switch
            {
                FilesystemKey => "Secure filesystem operations within allowed directories.",
                RagKey => "Search local knowledge bases.",
                OutboxKey => "Send messages via Slack, Telegram, Email, and KakaoTalk.",
                RunnerKey => "Schedule and run headless LLM tasks.",
                EssentialsKey => "Essential tools — HTTP, shell, JavaScript, file I/O, and environment info.",
                _ => "",
            },
        };

        // Runner requires "serve" subcommand for MCP stdio mode
        if (serverKey == RunnerKey)
            mcpConfig.Arguments = ["serve"];

        // RAG uses multi-KB serve mode with --base-path
        if (serverKey == RagKey)
            mcpConfig.Arguments = ["serve", "--base-path", KnowledgeBaseStore.BasePath];

        // Essentials: pass --search-engine arg if a non-default engine is configured
        if (serverKey == EssentialsKey
            && !string.IsNullOrEmpty(config.SearchEngine)
            && !config.SearchEngine.Equals("default", StringComparison.OrdinalIgnoreCase))
        {
            mcpConfig.Arguments = ["--search-engine", config.SearchEngine];
        }

        // Load environment variables from PasswordVault for built-in servers
        // (RAG no longer uses env vars — config.json + PasswordVault presets instead)
        if (serverKey != RagKey && config.EnvironmentVariableKeys is { Count: > 0 } keys)
        {
            mcpConfig.EnvironmentVariables = PasswordVaultHelper.LoadMcpEnvVars(mcpConfig.Id, keys);
        }

        return mcpConfig;
    }

    /// <summary>
    /// Resolves the path to a built-in server executable installed via dotnet tool.
    /// Falls back to the legacy <c>servers/</c> subfolder in the app directory.
    /// </summary>
    public static string GetServerExePath(string serverKey)
    {
        if (!ServerDefinitions.TryGetValue(serverKey, out var def))
            return "";

        var exeName = OperatingSystem.IsWindows() ? $"{def.ExeName}.exe" : def.ExeName;

        // Primary: dotnet tool install path
        var toolPath = Path.Combine(ToolInstallPath, exeName);
        if (File.Exists(toolPath))
            return toolPath;

        // Fallback: legacy bundled path
        return Path.Combine(AppContext.BaseDirectory, "servers", exeName);
    }

    /// <summary>
    /// Critical-path initialization: refreshes the version cache, installs any missing
    /// servers in parallel, and applies any pending updates queued by a previous launch.
    /// Designed to complete as fast as possible so MCP servers can spawn immediately after.
    /// </summary>
    public static async Task InitializeToolsAsync()
    {
        Directory.CreateDirectory(ToolInstallPath);

        // 1. Single "dotnet tool list" call → populate version cache
        await RefreshVersionCacheAsync();

        // 2. Install any missing executables in parallel
        await InstallMissingToolsAsync();

        // 3. Apply pending updates from a previous background check
        await ApplyPendingUpdatesAsync();
    }

    /// <summary>
    /// Background task: queries the NuGet API for newer versions of all built-in packages
    /// and writes a <c>pending-updates.json</c> file if updates are available.
    /// Shows an <see cref="AppNotification"/> (OS toast) when updates are found.
    /// Fully wrapped in try/catch — safe to fire-and-forget.
    /// </summary>
    public static async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            // Skip if pending updates are already queued (avoid duplicate notifications)
            if (File.Exists(PendingUpdatesPath)) return;

            // Throttle: debug = every launch, release = once per 24h
            if (!ShouldCheckForUpdates()) return;

            LoggingService.LogInfo("[BuiltIn] Background update check starting");

            // Query NuGet flat container API for all packages in parallel
            var updates = new List<PendingUpdateEntry>();
            var tasks = NuGetPackages.Select(async kv =>
            {
                var (serverKey, packageId) = kv;
                if (!_versionCache.TryGetValue(serverKey, out var currentVersion))
                    return (PendingUpdateEntry?)null;

                var latest = await GetLatestVersionAsync(packageId);
                if (latest is null) return null;

                if (!Version.TryParse(currentVersion, out var current)) return null;
                if (!Version.TryParse(latest, out var latestVer)) return null;

                return latestVer > current
                    ? new PendingUpdateEntry { Package = packageId, From = currentVersion, To = latest }
                    : null;
            }).ToArray();

            var results = await Task.WhenAll(tasks);
            updates.AddRange(results.Where(r => r is not null)!);

            if (updates.Count > 0)
            {
                await SavePendingUpdatesAsync(updates);
                ShowUpdateNotification(updates);
                LoggingService.LogInfo($"[BuiltIn] {updates.Count} update(s) queued for next launch");
            }
            else
            {
                LoggingService.LogInfo("[BuiltIn] All packages are up-to-date");
            }

            AppSettings.LastToolUpdateCheck = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Background update check failed: {ex.Message}");
        }
    }

    #endregion

    #region Private Helpers — Tool Management

    /// <summary>
    /// Installs any built-in server executables that are not yet present, in parallel.
    /// </summary>
    private static async Task InstallMissingToolsAsync()
    {
        var installTasks = new List<Task>();

        foreach (var (serverKey, packageId) in NuGetPackages)
        {
            if (!ServerDefinitions.TryGetValue(serverKey, out var def))
                continue;

            var exeName = OperatingSystem.IsWindows() ? $"{def.ExeName}.exe" : def.ExeName;
            var exePath = Path.Combine(ToolInstallPath, exeName);

            if (File.Exists(exePath))
                continue;

            installTasks.Add(InstallPackageAsync(serverKey, packageId, def.DisplayName));
        }

        if (installTasks.Count > 0)
        {
            LoggingService.LogInfo($"[BuiltIn] Installing {installTasks.Count} missing package(s)");
            await Task.WhenAll(installTasks);
        }
    }

    /// <summary>
    /// Installs a single dotnet tool package with notification.
    /// </summary>
    private static async Task InstallPackageAsync(string serverKey, string packageId, string displayName)
    {
        LoggingService.LogInfo($"[BuiltIn] Installing {packageId}");
        NotifyAction(packageId, "BuiltIn_Installing", displayName);

        try
        {
            var result = await RunDotnetToolAsync("install", packageId, ToolInstallPath);
            if (result == 0)
            {
                LoggingService.LogInfo($"[BuiltIn] {packageId} installed successfully");
                await RefreshVersionCacheAsync();
                var version = _versionCache.TryGetValue(serverKey, out var v) ? v : "?";
                NotifyInstallSuccess(displayName, version);
            }
            else
            {
                LoggingService.LogError($"[BuiltIn] {packageId} install failed (exit code {result})");
                NotifyInstallFailure(displayName);
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogError($"[BuiltIn] {packageId} install error: {ex.Message}");
            NotifyInstallFailure(displayName);
        }
    }

    /// <summary>
    /// Loads and applies pending updates from disk. Updates are run in parallel.
    /// Progress is shown via persistent notification. Failed items are discarded.
    /// </summary>
    private static async Task ApplyPendingUpdatesAsync()
    {
        var pending = await LoadPendingUpdatesAsync();
        if (pending is null || pending.Updates.Count == 0)
            return;

        // Filter out entries already at or above target version
        // (e.g., user manually upgraded via `dotnet tool update`)
        var actualUpdates = pending.Updates.Where(entry =>
        {
            var serverKey = NuGetPackages
                .FirstOrDefault(kv => kv.Value.Equals(entry.Package, StringComparison.OrdinalIgnoreCase))
                .Key;

            if (serverKey is not null
                && _versionCache.TryGetValue(serverKey, out var installedStr)
                && Version.TryParse(installedStr, out var installed)
                && Version.TryParse(entry.To, out var target)
                && installed >= target)
            {
                LoggingService.LogInfo(
                    $"[BuiltIn] Skipping update for {entry.Package}: already at {installedStr} (target {entry.To})");
                return false;
            }

            return true;
        }).ToList();

        if (actualUpdates.Count == 0)
        {
            LoggingService.LogInfo("[BuiltIn] All pending updates already satisfied, cleaning up");
            DeletePendingUpdates();
            return;
        }

        var total = actualUpdates.Count;
        LoggingService.LogInfo($"[BuiltIn] Applying {total} pending update(s)");

        var loader = TryGetResourceLoader();
        var progressTitle = loader?.GetString("BuiltIn_UpdatingProgress") ?? "Updating MCP packages ({0}/{1})";

        var token = NotificationCenter.Instance.PostPersistent(
            InfoBarSeverity.Informational,
            SafeFormat(progressTitle, [0, total]),
            string.Empty);

        var completed = 0;
        var updateTasks = actualUpdates.Select(async entry =>
        {
            try
            {
                var args = new List<string> { "tool", "update", entry.Package, "--tool-path", ToolInstallPath };
                if (!string.IsNullOrEmpty(entry.To))
                {
                    args.Add("--version");
                    args.Add(entry.To);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                foreach (var arg in args) psi.ArgumentList.Add(arg);

                using var process = Process.Start(psi);
                if (process is null) return;

                await process.WaitForExitAsync();

                var count = Interlocked.Increment(ref completed);
                NotificationCenter.Instance.Update(token,
                    title: SafeFormat(progressTitle, [count, total]));

                if (process.ExitCode == 0)
                    LoggingService.LogInfo($"[BuiltIn] Updated {entry.Package} {entry.From} → {entry.To}");
                else
                    LoggingService.LogWarning($"[BuiltIn] {entry.Package} update failed (exit code {process.ExitCode}), discarding");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref completed);
                LoggingService.LogWarning($"[BuiltIn] {entry.Package} update error: {ex.Message}, discarding");
            }
        }).ToArray();

        await Task.WhenAll(updateTasks);

        // Always delete pending file — failed items are discarded, background check will re-detect
        DeletePendingUpdates();

        // Refresh version cache after updates
        await RefreshVersionCacheAsync();

        // Dismiss progress, show completion
        NotificationCenter.Instance.Dismiss(token);

        var completeMsg = loader?.GetString("BuiltIn_UpdateComplete") ?? "MCP packages updated";
        NotificationCenter.Instance.Post(InfoBarSeverity.Success, completeMsg, string.Empty, 3000);
    }

    #endregion

    #region Private Helpers — Version & Updates

    /// <summary>
    /// Cached installed versions populated by <see cref="RefreshVersionCacheAsync"/>.
    /// Key: server key (e.g. "filesystem"), Value: version string (e.g. "0.5.0").
    /// </summary>
    private static readonly Dictionary<string, string> _versionCache = new();

    /// <summary>
    /// Returns the cached installed version for a built-in server, or <c>null</c> if unknown.
    /// Call <see cref="InitializeToolsAsync"/> first to populate the cache.
    /// </summary>
    public static string? GetInstalledVersion(string serverKey)
        => _versionCache.TryGetValue(serverKey, out var v) ? v : null;

    /// <summary>
    /// Determines whether built-in server update checks should run on this launch.
    /// Debug builds always check; Release builds check once per 24 hours.
    /// </summary>
    private static bool ShouldCheckForUpdates()
    {
#if DEBUG
        return true;
#else
        var last = AppSettings.LastToolUpdateCheck;
        return last is null || (DateTime.UtcNow - last.Value).TotalHours >= 24;
#endif
    }

    /// <summary>
    /// Refreshes <see cref="_versionCache"/> with a single <c>dotnet tool list</c> invocation.
    /// </summary>
    private static async Task RefreshVersionCacheAsync()
    {
        _versionCache.Clear();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "tool", "list", "--tool-path", ToolInstallPath },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };

            using var process = Process.Start(psi);
            if (process is null) return;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Build reverse lookup: packageId (lowercase) → serverKey
            var packageToKey = NuGetPackages.ToDictionary(
                kv => kv.Value.ToLowerInvariant(),
                kv => kv.Key);

            // Parse output lines: "package-id    version    commands"
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;

                if (packageToKey.TryGetValue(parts[0].ToLowerInvariant(), out var serverKey)
                    && Version.TryParse(parts[1], out _))
                {
                    _versionCache[serverKey] = parts[1];
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Failed to refresh version cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Queries the latest stable version of a NuGet package via the flat container API.
    /// </summary>
    private static async Task<string?> GetLatestVersionAsync(string packageId)
    {
        try
        {
            var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetString())
                .Where(v => v is not null && !v.Contains('-')) // exclude prerelease
                .LastOrDefault();
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Failed to check latest version for {packageId}: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Private Helpers — Pending Updates File

    /// <summary>
    /// Loads pending updates from disk, or returns <c>null</c> if the file doesn't exist or is invalid.
    /// </summary>
    private static async Task<PendingUpdatesFile?> LoadPendingUpdatesAsync()
    {
        try
        {
            if (!File.Exists(PendingUpdatesPath)) return null;
            var json = await File.ReadAllTextAsync(PendingUpdatesPath);
            return JsonSerializer.Deserialize(json, PendingUpdatesJsonContext.Default.PendingUpdatesFile);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Failed to load pending updates: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Saves pending updates to disk.
    /// </summary>
    private static async Task SavePendingUpdatesAsync(List<PendingUpdateEntry> updates)
    {
        try
        {
            var file = new PendingUpdatesFile
            {
                CheckedAt = DateTime.UtcNow,
                Updates = updates,
            };
            var json = JsonSerializer.Serialize(file, PendingUpdatesJsonContext.Default.PendingUpdatesFile);
            await File.WriteAllTextAsync(PendingUpdatesPath, json);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Failed to save pending updates: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes the pending updates file from disk.
    /// </summary>
    private static void DeletePendingUpdates()
    {
        try
        {
            if (File.Exists(PendingUpdatesPath))
                File.Delete(PendingUpdatesPath);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Failed to delete pending updates file: {ex.Message}");
        }
    }

    #endregion

    #region Private Helpers — Process Execution

    /// <summary>
    /// Runs a dotnet tool command (install/update) and returns the exit code.
    /// </summary>
    private static async Task<int> RunDotnetToolAsync(string action, string packageId, string toolPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "tool", action, packageId, "--tool-path", toolPath },
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi);
        if (process is null) return -1;

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    #endregion

    #region Private Helpers — Notifications

    /// <summary>
    /// Shows an in-app notification about available updates (for next launch).
    /// </summary>
    private static void ShowUpdateNotification(List<PendingUpdateEntry> updates)
    {
        var loader = TryGetResourceLoader();

        string title;
        string body;

        if (updates.Count == 1)
        {
            var u = updates[0];
            title = loader?.GetString("BuiltIn_UpdateAvailable_Title")
                    ?? "MCP package update ready";
            body = $"{u.Package} {u.From} → {u.To}";
        }
        else
        {
            var first = updates[0];
            title = loader?.GetString("BuiltIn_UpdateAvailable_Title")
                    ?? "MCP package updates ready";
            var template = loader?.GetString("BuiltIn_UpdateAvailable_Body")
                           ?? "{0} {1} → {2} and {3} more";
            body = SafeFormat(template, [first.Package, first.From, first.To, updates.Count - 1]);
        }

        NotificationCenter.Instance.Post(InfoBarSeverity.Informational, title, body, 8000);
    }

    /// <summary>
    /// Posts a success notification for server installation.
    /// </summary>
    private static void NotifyInstallSuccess(string serverName, string version)
    {
        var loader = TryGetResourceLoader();
        var template = loader?.GetString("BuiltIn_InstallSuccess") ?? "{0} v{1} server installed";

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Success,
            SafeFormat(template, [serverName, version]),
            loader?.GetString("BuiltIn_InstallSuccessMessage") ?? "Configure workspace folders in Profile settings.",
            5000);
    }

    /// <summary>
    /// Posts an error notification for server installation failure.
    /// </summary>
    private static void NotifyInstallFailure(string serverName)
    {
        var loader = TryGetResourceLoader();
        var template = loader?.GetString("BuiltIn_InstallFailed") ?? "Failed to install {0}";

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Error,
            SafeFormat(template, [serverName]),
            loader?.GetString("BuiltIn_InstallFailedMessage") ?? "Check your internet connection and try again.",
            8000);
    }

    /// <summary>
    /// Posts an informational notification for install/update actions using a localized format string.
    /// </summary>
    private static void NotifyAction(string packageId, string resourceKey, params string[] args)
    {
        var loader = TryGetResourceLoader();

        var template = loader?.GetString(resourceKey) ?? $"{packageId} — {resourceKey}";
        var message = SafeFormat(template, args);

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Informational,
            message,
            string.Empty,
            4000);
    }

    /// <summary>
    /// Formats a localized notification message, falling back gracefully on format errors.
    /// </summary>
    private static string SafeFormat(string template, object[] args)
    {
        if (args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // Prevent a bad resource string from crashing initialization
            return $"{template} [{string.Join(", ", args)}]";
        }
    }

    /// <summary>
    /// Safely creates a ResourceLoader, returning <c>null</c> if unavailable.
    /// </summary>
    private static Windows.ApplicationModel.Resources.ResourceLoader? TryGetResourceLoader()
    {
        try { return new Windows.ApplicationModel.Resources.ResourceLoader(); }
        catch { return null; }
    }

    #endregion

    #region Public Helpers

    /// <summary>
    /// Determines whether a tool from a built-in server requires user confirmation.
    /// Read-only tools (list, read, search, get_info) do not require confirmation.
    /// Write/modify/delete tools require confirmation.
    /// </summary>
    public static bool? GetRequiresConfirmation(string toolName)
    {
        return !ReadOnlyToolNames.Contains(toolName);
    }

    /// <summary>
    /// Gets the display name for a built-in server key.
    /// </summary>
    public static string GetDisplayName(string serverKey)
    {
        return ServerDefinitions.TryGetValue(serverKey, out var def)
            ? def.DisplayName
            : serverKey;
    }

    /// <summary>
    /// Returns <see langword="true"/> if the built-in server is shared across all tabs (not per-tab).
    /// Shared servers do not require folder arguments.
    /// </summary>
    public static bool IsSharedServer(string serverKey) => serverKey is EssentialsKey or OutboxKey or RunnerKey or RagKey;

    /// <summary>
    /// Returns <see langword="true"/> if the given command matches a built-in server executable.
    /// Used to prevent users from manually adding a server that duplicates a built-in one.
    /// </summary>
    public static bool IsBuiltInCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        var fileName = Path.GetFileNameWithoutExtension(command);
        return ServerDefinitions.Values.Any(d =>
            d.ExeName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}

#region Pending Updates Models

/// <summary>
/// Represents the pending-updates.json file structure.
/// </summary>
internal sealed class PendingUpdatesFile
{
    [JsonPropertyName("checked_at")]
    public DateTime CheckedAt { get; set; }

    [JsonPropertyName("updates")]
    public List<PendingUpdateEntry> Updates { get; set; } = [];
}

/// <summary>
/// A single pending package update entry.
/// </summary>
internal sealed class PendingUpdateEntry
{
    [JsonPropertyName("package")]
    public string Package { get; set; } = "";

    [JsonPropertyName("from")]
    public string From { get; set; } = "";

    [JsonPropertyName("to")]
    public string To { get; set; } = "";
}

/// <summary>
/// Source-generated JSON context for pending updates serialization.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PendingUpdatesFile))]
internal partial class PendingUpdatesJsonContext : JsonSerializerContext
{
}

#endregion
