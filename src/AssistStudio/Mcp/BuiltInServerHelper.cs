using System.Diagnostics;
using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using FieldCure.Ai.Providers.Models;
using Microsoft.UI.Xaml.Controls;

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
    /// Maps server keys to their NuGet package IDs and required minimum versions.
    /// Bump the version when the app requires a newer server release.
    /// </summary>
    private static readonly Dictionary<string, (string PackageId, string RequiredVersion)> NuGetPackages = new()
    {
        [FilesystemKey] = ("FieldCure.Mcp.Filesystem", "0.5.0"),
        [RagKey] = ("FieldCure.Mcp.Rag", "0.10.1"),
        [OutboxKey] = ("FieldCure.Mcp.Outbox", "0.4.0"),
        [RunnerKey] = ("FieldCure.AssistStudio.Runner", "0.3.0"),
        [EssentialsKey] = ("FieldCure.Mcp.Essentials", "0.1.0"),
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
    public const string RagDisplayName = "Knowledge Folders";

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
        "search_documents", "get_document_chunk",
        // Outbox — add_channel opens a subprocess console for credential input
        "list_channels", "add_channel",
        // Runner
        "list_tasks", "get_execution_status", "get_task_history",
        // Essentials
        "get_environment", "run_javascript",
    ];

    /// <summary>
    /// Memory server tool names — excluded from Essentials, shown under Memory toggle.
    /// </summary>
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

    #endregion

    #region Methods

    /// <summary>
    /// Returns the required NuGet package version for a built-in server key, or null if not found.
    /// </summary>
    public static string? GetRequiredVersion(string serverKey)
        => NuGetPackages.TryGetValue(serverKey, out var pkg) ? pkg.RequiredVersion : null;

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

        // Folder-based servers (Filesystem, RAG) require at least one folder
        if (!IsSharedServer(serverKey) && config.Folders.Count == 0)
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
                RagKey => "Index and search local documents.",
                OutboxKey => "Send messages via Slack, Telegram, Email, and KakaoTalk.",
                RunnerKey => "Schedule and run headless LLM tasks.",
                EssentialsKey => "Essential tools — HTTP, shell, JavaScript, file I/O, and environment info.",
                _ => "",
            },
        };

        // Runner requires "serve" subcommand for MCP stdio mode
        if (serverKey == RunnerKey)
            mcpConfig.Arguments = ["serve"];

        // Load environment variables from PasswordVault for built-in servers (e.g., RAG embedding config)
        if (config.EnvironmentVariableKeys is { Count: > 0 } keys)
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
    /// Ensures all built-in server tools are installed and up-to-date via <c>dotnet tool</c>.
    /// Installs missing servers and updates outdated ones based on <see cref="NuGetPackages"/> versions.
    /// </summary>
    public static async Task EnsureInstalledAsync()
    {
        Directory.CreateDirectory(ToolInstallPath);

        foreach (var (serverKey, (packageId, requiredVersion)) in NuGetPackages)
        {
            if (!ServerDefinitions.TryGetValue(serverKey, out var def))
                continue;

            var exeName = OperatingSystem.IsWindows() ? $"{def.ExeName}.exe" : def.ExeName;
            var exePath = Path.Combine(ToolInstallPath, exeName);
            var required = Version.Parse(requiredVersion);

            if (File.Exists(exePath))
            {
                // Check installed version
                var installed = await GetInstalledVersionAsync(packageId);
                if (installed is not null && installed >= required)
                {
                    LoggingService.LogInfo($"[BuiltIn] {packageId} v{installed} up-to-date (required {requiredVersion})");
                    continue;
                }

                // Update needed
                LoggingService.LogInfo($"[BuiltIn] Updating {packageId}: v{installed} → v{requiredVersion}");
                NotifyAction(packageId, "BuiltIn_Updating", packageId, installed?.ToString() ?? "?", requiredVersion);
                try
                {
                    var result = await RunDotnetToolAsync("update", packageId, ToolInstallPath);
                    if (result == 0)
                    {
                        LoggingService.LogInfo($"[BuiltIn] {packageId} updated successfully");
                        NotifyAction(packageId, "BuiltIn_Updated", packageId, installed?.ToString() ?? "?", requiredVersion);
                    }
                    else
                    {
                        LoggingService.LogWarning($"[BuiltIn] {packageId} update failed (exit code {result})");
                        NotifyInstallFailure(def.DisplayName);
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError($"[BuiltIn] {packageId} update error: {ex.Message}");
                    NotifyInstallFailure(def.DisplayName);
                }
                continue;
            }

            // Fresh install
            LoggingService.LogInfo($"[BuiltIn] Installing {packageId} v{requiredVersion}");
            NotifyAction(packageId, "BuiltIn_Installing", packageId, requiredVersion);
            try
            {
                var result = await RunDotnetToolAsync("install", packageId, ToolInstallPath);
                if (result == 0)
                {
                    LoggingService.LogInfo($"[BuiltIn] {packageId} installed successfully");
                    NotifyInstallSuccess(def.DisplayName);
                }
                else
                {
                    LoggingService.LogWarning($"[BuiltIn] {packageId} install failed (exit code {result})");
                    NotifyInstallFailure(def.DisplayName);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError($"[BuiltIn] {packageId} install error: {ex.Message}");
                NotifyInstallFailure(def.DisplayName);
            }
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Gets the installed version of a dotnet tool by parsing <c>dotnet tool list</c> output.
    /// Returns <c>null</c> if the tool is not found or the version cannot be parsed.
    /// </summary>
    private static async Task<Version?> GetInstalledVersionAsync(string packageId)
    {
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
            if (process is null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            // Parse output lines: "package-id    version    commands"
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2
                    && parts[0].Equals(packageId, StringComparison.OrdinalIgnoreCase)
                    && Version.TryParse(parts[1], out var version))
                {
                    return version;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[BuiltIn] Failed to check version for {packageId}: {ex.Message}");
        }

        return null;
    }

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

    /// <summary>
    /// Posts a success notification for server installation.
    /// </summary>
    private static void NotifyInstallSuccess(string serverName)
    {
        Windows.ApplicationModel.Resources.ResourceLoader? loader = null;
        try { loader = new Windows.ApplicationModel.Resources.ResourceLoader(); }
        catch { /* fallback */ }

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Success,
            string.Format(loader?.GetString("BuiltIn_InstallSuccess") ?? "{0} server installed", serverName),
            loader?.GetString("BuiltIn_InstallSuccessMessage") ?? "Configure workspace folders in Profile settings.",
            5000);
    }

    /// <summary>
    /// Posts an error notification for server installation failure.
    /// </summary>
    private static void NotifyInstallFailure(string serverName)
    {
        Windows.ApplicationModel.Resources.ResourceLoader? loader = null;
        try { loader = new Windows.ApplicationModel.Resources.ResourceLoader(); }
        catch { /* fallback */ }

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Error,
            string.Format(loader?.GetString("BuiltIn_InstallFailed") ?? "Failed to install {0}", serverName),
            loader?.GetString("BuiltIn_InstallFailedMessage") ?? "Check your internet connection and try again.",
            8000);
    }

    /// <summary>
    /// Posts an informational notification for install/update actions using a localized format string.
    /// </summary>
    private static void NotifyAction(string packageId, string resourceKey, params string[] args)
    {
        Windows.ApplicationModel.Resources.ResourceLoader? loader = null;
        try { loader = new Windows.ApplicationModel.Resources.ResourceLoader(); }
        catch { /* fallback */ }

        var template = loader?.GetString(resourceKey) ?? $"{packageId} — {resourceKey}";
        var message = args.Length > 0 ? string.Format(template, args) : template;

        NotificationCenter.Instance.Post(
            InfoBarSeverity.Informational,
            message,
            string.Empty,
            4000);
    }

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
    public static bool IsSharedServer(string serverKey) => serverKey is EssentialsKey or OutboxKey or RunnerKey;

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

    #endregion // Private Helpers
}
