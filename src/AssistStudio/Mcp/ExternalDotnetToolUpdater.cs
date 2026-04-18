using System.Diagnostics;
using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;

namespace AssistStudio.Mcp;

/// <summary>
/// Auto-updates user-added (non-built-in) MCP servers that are installed as global
/// .NET tools. Runs at app startup <b>before</b> external servers are connected so an
/// upgrade cannot collide with a running subprocess file lock.
/// </summary>
/// <remarks>
/// Built-in servers use <see cref="BuiltInServerHelper"/> which installs into its own
/// <c>--tool-path</c> and schedules updates via <c>pending-updates.json</c> because the
/// tool binaries may be in-use at check time. External servers follow a simpler path:
/// they are installed globally (<c>dotnet tool install -g</c>), and because this sweep
/// fires before any external MCP subprocess is spawned for the session, we can run
/// <c>dotnet tool update -g</c> inline and rely on dotnet itself to no-op when already
/// at the latest version.
///
/// The command→packageId mapping is recovered from <c>dotnet tool list -g</c> output.
/// Commands that are not installed as global tools (paths, runners, missing binaries)
/// are silently skipped.
/// </remarks>
public static class ExternalDotnetToolUpdater
{
    /// <summary>
    /// Runs an update sweep for every stdio MCP server whose command resolves to a
    /// globally-installed .NET tool. Best-effort: failures are logged and do not block
    /// startup.
    /// </summary>
    public static async Task CheckAndUpdateAsync(IEnumerable<McpServerConfig> configs)
    {
        try
        {
            var candidateCommands = configs
                .Where(c => c.TransportType == McpTransportType.Stdio)
                .Select(c => c.Command?.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .Select(c => c!)
                .Where(c => !McpCommandInstaller.KnownRunners.Contains(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (candidateCommands.Count == 0) return;

            var globalTools = await GetGlobalToolCommandMapAsync();
            if (globalTools.Count == 0) return;

            var toUpdate = candidateCommands
                .Where(cmd => globalTools.ContainsKey(cmd))
                .Select(cmd => (Command: cmd, PackageId: globalTools[cmd]))
                .ToList();

            if (toUpdate.Count == 0)
            {
                LoggingService.LogInfo(
                    "[External] No configured external server commands map to global dotnet tools; " +
                    "nothing to update.");
                return;
            }

            LoggingService.LogInfo(
                $"[External] Checking NuGet updates for {toUpdate.Count} external dotnet tool(s): " +
                $"{string.Join(", ", toUpdate.Select(t => t.PackageId))}");

            var updateTasks = toUpdate
                .Select(entry => UpdateToolAsync(entry.Command, entry.PackageId))
                .ToList();

            await Task.WhenAll(updateTasks);
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning(
                $"[External] Dotnet tool update sweep failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs <c>dotnet tool list -g</c> and builds a case-insensitive map from the first
    /// command listed for each tool back to its NuGet package ID. Returns an empty map
    /// on any error so callers can short-circuit the sweep.
    /// </summary>
    private static async Task<Dictionary<string, string>> GetGlobalToolCommandMapAsync()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "tool", "list", "-g" },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return map;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();
            if (proc.ExitCode != 0) return map;

            // Expected format (3 columns, whitespace-separated):
            //   Package Id                     Version      Commands
            //   ---------------------------------------------------------
            //   fieldcure.mcp.publicdata.kr    1.0.0        fieldcure-mcp-publicdata-kr
            //
            // Tools can list multiple comma-separated commands — record each as a
            // separate map entry pointing to the same package id.
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("Package Id", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith('-')) continue;

                var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                if (!Version.TryParse(parts[1], out _)) continue;

                var packageId = parts[0];
                foreach (var cmd in parts.Skip(2).SelectMany(s => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
                {
                    map[cmd] = packageId;
                }
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[External] Failed to enumerate global tools: {ex.Message}");
        }

        return map;
    }

    /// <summary>
    /// Runs <c>dotnet tool update -g &lt;packageId&gt;</c>. Logs success or failure;
    /// does not throw so a single failing tool cannot block the parallel sweep.
    /// </summary>
    private static async Task UpdateToolAsync(string command, string packageId)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { "tool", "update", "-g", packageId },
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc is null) return;

            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync();
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (proc.ExitCode == 0)
            {
                // dotnet prints "Tool '...' was reinstalled with the stable version" on update
                // and "Tool '...' was already installed with the requested version" on no-op;
                // the exit code is 0 in both cases. Log the stdout line so the log shows what
                // changed (if anything).
                var firstLine = stdout.Split('\n')
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => l.Length > 0);
                LoggingService.LogInfo($"[External] {packageId} ({command}): {firstLine ?? "up-to-date"}");
            }
            else
            {
                LoggingService.LogWarning(
                    $"[External] {packageId} update failed (exit={proc.ExitCode}): " +
                    (stderr.Trim().Length > 0 ? stderr.Trim() : stdout.Trim()));
            }
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[External] {packageId} update exception: {ex.Message}");
        }
    }
}
