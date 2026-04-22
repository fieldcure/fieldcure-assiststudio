using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// Static helper for built-in MCP server configuration resolution and
/// <see cref="McpServerConfig"/> construction.
/// </summary>
public static class BuiltInServerHelper
{
    /// <summary>
    /// Shared <see cref="ResourceLoader"/> for localized strings used in built-in
    /// server notifications. Initialized once at type load; thread-safe and safe to
    /// call from any thread (no <c>CoreWindow</c> dependency).
    /// </summary>
    private static readonly ResourceLoader Res = new();

    #region Constants

    /// <summary>
    /// Maps server keys to their NuGet package IDs. Packages are run via <c>dnx</c>
    /// (NuGet's npx-equivalent, .NET 10+), which fetches and executes them directly
    /// from NuGet — no global install step is required.
    /// </summary>
    private static readonly Dictionary<string, string> NuGetPackages = new()
    {
        [FilesystemKey] = "FieldCure.Mcp.Filesystem",
        [RagKey] = "FieldCure.Mcp.Rag",
        [OutboxKey] = "FieldCure.Mcp.Outbox",
        [RunnerKey] = "FieldCure.AssistStudio.Runner",
        [EssentialsKey] = "FieldCure.Mcp.Essentials",
    };

    /// <summary>
    /// Major-version ranges for built-in packages. <c>dnx</c> resolves the
    /// latest available version within the range on every invocation (subject
    /// to its own cache). Bumping a major here is an intentional AssistStudio
    /// release-coupled decision — new majors ship breaking changes and must be
    /// validated against the host before being picked up.
    /// </summary>
    private static readonly Dictionary<string, string> MajorVersionRanges = new()
    {
        [FilesystemKey] = "1.*",
        [RagKey] = "2.*",
        [OutboxKey] = "2.*",
        [RunnerKey] = "1.*",
        [EssentialsKey] = "2.*",
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
    public const string RagDisplayName = "Knowledge Base";

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
        // RAG (start_reindex is NOT read-only — requires confirmation)
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
    /// Returns <see langword="null"/> if the server is disabled or has no folders
    /// (for folder-based servers). The command is always <c>dnx</c>, which fetches
    /// the package from NuGet on first use and caches subsequent invocations.
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

        var (command, prefixArgs) = GetLaunchSpec(serverKey);
        if (string.IsNullOrEmpty(command))
            return null;

        // Server-specific trailing args
        List<string> tailArgs = serverKey switch
        {
            RunnerKey => ["serve"],
            RagKey => ["serve", "--base-path", KnowledgeBaseStore.BasePath],
            EssentialsKey when !string.IsNullOrEmpty(config.SearchEngine)
                && !config.SearchEngine.Equals("default", StringComparison.OrdinalIgnoreCase)
                => ["--search-engine", config.SearchEngine],
            _ => config.Folders.Count > 0 ? [.. config.Folders] : [],
        };

        var mcpConfig = new McpServerConfig
        {
            Id = $"builtin_{serverKey}",
            Name = def.DisplayName,
            TransportType = McpTransportType.Stdio,
            Command = command,
            Arguments = [.. prefixArgs, .. tailArgs],
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

        // Load environment variables from PasswordVault for built-in servers
        if (config.EnvironmentVariableKeys is { Count: > 0 } keys)
        {
            mcpConfig.EnvironmentVariables = PasswordVaultHelper.LoadMcpEnvVars(mcpConfig.Id, keys);
        }

        // RAG reads API keys from environment variables (no longer from PasswordVault directly).
        // Inject all known provider keys so any KB config can resolve its apiKeyPreset.
        if (serverKey == RagKey)
        {
            mcpConfig.EnvironmentVariables ??= new Dictionary<string, string>();
            InjectRagApiKeys(mcpConfig.EnvironmentVariables);
        }

        // Essentials reads search/Wolfram API keys from environment variables.
        // Load from the shared McpEnv_{serverId}_{key} slot so Runner-spawned Essentials
        // picks up the same keys (ADR-001).
        if (serverKey == EssentialsKey)
        {
            mcpConfig.EnvironmentVariables ??= new Dictionary<string, string>();
            foreach (var (k, v) in PasswordVaultHelper.LoadMcpEnvVars(mcpConfig.Id, EssentialsEnvKeys))
                mcpConfig.EnvironmentVariables[k] = v;
        }

        return mcpConfig;
    }

    /// <summary>
    /// Rebuilds the <c>Arguments</c>, <c>EnvironmentVariables</c>, and <c>Command</c>
    /// on a built-in server config from current <see cref="AppSettings"/> and
    /// PasswordVault state, so a reconnect picks up changes made since the first
    /// spawn (e.g., rotated Provider keys for RAG, a new Wolfram AppID for Essentials).
    /// <para>
    /// Mutates <paramref name="target"/> in place because <see cref="McpServerConnection.Config"/>
    /// is get-only. Other fields (<c>Id</c>, <c>Name</c>, <c>Description</c>, <c>IsEnabled</c>)
    /// are intentionally not overwritten — the Reconnect contract is "refresh dynamic
    /// state, preserve identity".
    /// </para>
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <paramref name="target"/> was identified as an enabled
    /// built-in and its dynamic fields were updated; <see langword="false"/> when the
    /// id lacks the <c>builtin_</c> prefix, the server is disabled, or
    /// <see cref="CreateMcpServerConfig"/> would have returned <see langword="null"/>
    /// (e.g., RAG with no knowledge base).
    /// </returns>
    public static bool TryRebuildBuiltInConfig(McpServerConfig target)
    {
        const string prefix = "builtin_";
        if (target.Id is null || !target.Id.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var serverKey = target.Id[prefix.Length..];
        if (!AppSettings.BuiltInServers.TryGetValue(serverKey, out var config) || config is null)
            return false;

        var rebuilt = CreateMcpServerConfig(serverKey, config);
        if (rebuilt is null)
            return false;

        target.Command = rebuilt.Command;
        target.Arguments = rebuilt.Arguments;
        target.EnvironmentVariables = rebuilt.EnvironmentVariables;
        return true;
    }

    /// <summary>
    /// Returns the <c>dnx</c> invocation spec for a built-in server. Command is
    /// the literal string <c>"dnx"</c> — MCP stdio transports resolve it through
    /// the shell so PATHEXT lookup works. For direct <see cref="Process.Start"/>
    /// callers (which do not search PATHEXT), use
    /// <see cref="GetLaunchSpecForProcess"/> which substitutes a resolved
    /// absolute path to <c>dnx.cmd</c>/<c>dnx</c>. Prefix args pin the package
    /// id with its major-version range (e.g. <c>FieldCure.Mcp.Rag@2.*</c>)
    /// followed by <c>--yes</c> to suppress the first-run install prompt.
    /// </summary>
    public static (string Command, string[] PrefixArgs) GetLaunchSpec(string serverKey)
    {
        if (!NuGetPackages.TryGetValue(serverKey, out var packageId)
            || !MajorVersionRanges.TryGetValue(serverKey, out var range))
            return ("", []);

        return ("dnx", [$"{packageId}@{range}", "--yes"]);
    }

    /// <summary>
    /// Variant of <see cref="GetLaunchSpec"/> for code paths that spawn the
    /// built-in server with <see cref="Process.Start(ProcessStartInfo)"/> and
    /// <c>UseShellExecute=false</c>. On Windows <c>dnx</c> ships as a
    /// <c>.cmd</c> shim which <c>Process.Start</c> will not find via bare name,
    /// so we substitute the resolved absolute path. Returns an empty command
    /// when <c>dnx</c> is not on PATH — callers should log and skip.
    /// </summary>
    public static (string Command, string[] PrefixArgs) GetLaunchSpecForProcess(string serverKey)
    {
        var (_, prefixArgs) = GetLaunchSpec(serverKey);
        if (prefixArgs.Length == 0) return ("", []);

        var dnx = _dnxPath.Value;
        return string.IsNullOrEmpty(dnx) ? ("", []) : (dnx, prefixArgs);
    }

    /// <summary>
    /// Cached absolute path to the <c>dnx</c> launcher. On Windows <c>dnx</c>
    /// ships as a <c>.cmd</c> shim; <see cref="Process.Start(ProcessStartInfo)"/>
    /// with <c>UseShellExecute=false</c> only searches PATH for <c>.exe</c> by
    /// default, so we resolve the full path once here and reuse it.
    /// </summary>
    private static readonly Lazy<string?> _dnxPath = new(ResolveDnxPath);

    /// <summary>
    /// Walks the PATH environment variable looking for a <c>dnx</c> launcher.
    /// Returns <see langword="null"/> when not found (e.g. user has not installed
    /// the .NET 10 SDK); callers should handle the absence gracefully.
    /// </summary>
    private static string? ResolveDnxPath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        string[] extensions = OperatingSystem.IsWindows()
            ? [".cmd", ".exe", ".bat", ".ps1"]
            : [""];

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, $"dnx{ext}");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Populates the version cache by querying NuGet for the latest version of each
    /// built-in package within its pinned major range. Fast fire-and-forget — does
    /// not block startup beyond the HTTP timeout. <c>dnx</c> handles actual package
    /// fetching lazily on first server spawn.
    /// </summary>
    public static async Task InitializeToolsAsync()
    {
        await RefreshVersionCacheAsync();
    }

    /// <summary>
    /// Background refresh of the version cache. Notifies the user when a package's
    /// latest-in-range version has advanced since the last check so they know a new
    /// build will be picked up on next server spawn (dnx auto-resolves within the
    /// range). Fully wrapped in try/catch — safe to fire-and-forget.
    /// </summary>
    public static async Task CheckForUpdatesInBackgroundAsync()
    {
        try
        {
            // Throttle: debug = every launch, release = once per 24h
            if (!ShouldCheckForUpdates()) return;

            LoggingService.LogInfo("[BuiltIn] Background version check starting");

            var previous = new Dictionary<string, string>(_versionCache);
            await RefreshVersionCacheAsync();

            var bumped = new List<(string Package, string From, string To)>();
            foreach (var (serverKey, latest) in _versionCache)
            {
                if (!previous.TryGetValue(serverKey, out var prev) || prev == latest)
                    continue;
                if (!Version.TryParse(prev, out var prevVer) || !Version.TryParse(latest, out var latestVer))
                    continue;
                if (latestVer <= prevVer)
                    continue;
                if (!NuGetPackages.TryGetValue(serverKey, out var packageId))
                    continue;
                bumped.Add((packageId, prev, latest));
            }

            if (bumped.Count > 0)
            {
                ShowUpdateNotification(bumped);
                LoggingService.LogInfo($"[BuiltIn] {bumped.Count} package(s) advanced within range");
            }
            else
            {
                LoggingService.LogInfo("[BuiltIn] No version changes detected");
            }

            AppSettings.LastToolUpdateCheck = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning(
                $"[BuiltIn] Background version check failed: {ex.GetType().FullName}: " +
                $"{ex.Message}{(ex.InnerException is null ? "" : $" → {ex.InnerException.GetType().Name}: {ex.InnerException.Message}")}");
            LoggingService.LogException(ex);
        }
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
    /// Refreshes <see cref="_versionCache"/> by querying NuGet for the latest
    /// stable version of each built-in package within its pinned major range.
    /// Called at startup and periodically thereafter; actual package download is
    /// handled by <c>dnx</c> on first invocation.
    /// </summary>
    private static async Task RefreshVersionCacheAsync()
    {
        var tasks = NuGetPackages.Select(async kv =>
        {
            var (serverKey, packageId) = kv;
            if (!MajorVersionRanges.TryGetValue(serverKey, out var range))
                return ((string, string)?)null;

            var latest = await GetLatestVersionInMajorAsync(packageId, range);
            return latest is null ? null : ((string, string)?)(serverKey, latest);
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        foreach (var item in results)
        {
            if (item is (string key, string ver))
                _versionCache[key] = ver;
        }
    }

    /// <summary>
    /// Queries NuGet's flat container API for the latest stable version of a
    /// package whose <see cref="Version.Major"/> matches the given range
    /// (e.g. <c>2.*</c> → pick the max version with Major == 2). Prerelease
    /// versions are excluded.
    /// </summary>
    private static async Task<string?> GetLatestVersionInMajorAsync(string packageId, string majorRange)
    {
        try
        {
            // Parse "2.*" → 2
            var dotStar = majorRange.IndexOf(".*", StringComparison.Ordinal);
            if (dotStar <= 0 || !int.TryParse(majorRange.AsSpan(0, dotStar), out var major))
                return null;

            var url = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var best = doc.RootElement
                .GetProperty("versions")
                .EnumerateArray()
                .Select(v => v.GetString())
                .Where(v => !string.IsNullOrEmpty(v) && !v!.Contains('-'))
                .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
                .Where(v => v is not null && v.Major == major)
                .Max();

            return best?.ToString();
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning(
                $"[BuiltIn] Failed to check latest {majorRange} version for {packageId}: " +
                $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region Private Helpers — Notifications

    /// <summary>
    /// Shows an in-app notification listing packages whose latest-in-range
    /// version advanced since the previous check. dnx will pick up the new
    /// build automatically on the next server spawn.
    /// </summary>
    private static void ShowUpdateNotification(List<(string Package, string From, string To)> bumped)
    {
        string title;
        string body;

        if (bumped.Count == 1)
        {
            var u = bumped[0];
            title = Res.GetString("BuiltIn_UpdateAvailable_Title")
                    ?? "MCP package update ready";
            body = $"{u.Package} {u.From} → {u.To}";
        }
        else
        {
            var first = bumped[0];
            title = Res.GetString("BuiltIn_UpdateAvailable_Title")
                    ?? "MCP package updates ready";
            var template = Res.GetString("BuiltIn_UpdateAvailable_Body")
                           ?? "{0} {1} → {2} and {3} more";
            body = SafeFormat(template, [first.Package, first.From, first.To, bumped.Count - 1]);
        }

        NotificationCenter.Instance.Post(InfoBarSeverity.Informational, title, body, 8000);
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
            return $"{template} [{string.Join(", ", args)}]";
        }
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
    /// Environment variable keys that the Essentials server consumes.
    /// Stored under the shared <c>McpEnv_builtin_essentials_{key}</c> PasswordVault slot
    /// so Runner-spawned Essentials picks up the same values via its own
    /// <c>CredentialService.GetMcpEnvVar</c> lookup (ADR-001).
    /// </summary>
    public static readonly string[] EssentialsEnvKeys =
    [
        "SERPER_API_KEY",
        "TAVILY_API_KEY",
        "SERPAPI_API_KEY",
        "WOLFRAM_APPID",
    ];

    /// <summary>
    /// Injects API keys from PasswordVault into a dictionary for the RAG process.
    /// The RAG process resolves keys via environment variables instead of
    /// accessing PasswordVault directly, keeping it platform-agnostic.
    /// </summary>
    private static void InjectRagApiKeys(IDictionary<string, string> envVars)
    {
        (string presetName, string envVarName)[] mappings =
        [
            ("OpenAI", "OPENAI_API_KEY"),
            ("Claude", "ANTHROPIC_API_KEY"),
            ("Gemini", "GEMINI_API_KEY"),
            ("Voyage", "VOYAGE_API_KEY"),
            ("Groq", "GROQ_API_KEY"),
        ];

        foreach (var (preset, envVar) in mappings)
        {
            var key = PasswordVaultHelper.LoadApiKey(preset);
            if (!string.IsNullOrEmpty(key))
                envVars[envVar] = key;
        }
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
    /// Returns <see langword="true"/> if the given stdio invocation matches a
    /// built-in server (<c>dnx</c> launching one of our package ids). Used to
    /// prevent users from manually adding a server that duplicates a built-in.
    /// </summary>
    public static bool IsBuiltInCommand(string? command, IReadOnlyList<string>? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(command)) return false;

        // Legacy: direct exe name match (pre-dnx installs)
        var fileName = Path.GetFileNameWithoutExtension(command);
        if (ServerDefinitions.Values.Any(d =>
            d.ExeName.Equals(fileName, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Current: dnx <packageId>@range
        if (!command.Equals("dnx", StringComparison.OrdinalIgnoreCase) || arguments is null || arguments.Count == 0)
            return false;

        var first = arguments[0];
        var at = first.IndexOf('@');
        var pkg = at > 0 ? first[..at] : first;
        return NuGetPackages.Values.Any(p => p.Equals(pkg, StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}
