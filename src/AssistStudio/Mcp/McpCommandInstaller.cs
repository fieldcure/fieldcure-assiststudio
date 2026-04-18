using System.Diagnostics;
using System.Text;
using AssistStudio.Helpers;
using FieldCure.AssistStudio.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace AssistStudio.Mcp;

/// <summary>
/// Pre-flight check for MCP stdio server commands. If the configured command is not
/// resolvable on PATH (and is not a known package runner such as npx/uvx), offers to
/// install it as a .NET global tool via <c>dotnet tool install -g</c>.
/// </summary>
/// <remarks>
/// Motivation: unlike <c>npx</c>/<c>uvx</c>, <c>dotnet</c> tools do not auto-install on
/// first invocation. Without this gate, attempting to launch an uninstalled tool command
/// surfaces as a confusing "server shut down unexpectedly" error from the MCP transport
/// layer instead of a clear "command not found" message.
/// </remarks>
public static class McpCommandInstaller
{
    /// <summary>
    /// Commands whose presence cannot be usefully verified with <c>where.exe</c> because
    /// they are package runners or shells whose behaviour depends on their arguments.
    /// These skip the pre-flight check and are passed through to the MCP transport.
    /// Also reused by <see cref="ExternalDotnetToolUpdater"/> to exclude non-dotnet-tool
    /// commands from the app-start update sweep.
    /// </summary>
    internal static readonly HashSet<string> KnownRunners = new(StringComparer.OrdinalIgnoreCase)
    {
        "npx", "uvx", "node", "python", "python3", "docker",
        "cmd", "powershell", "pwsh", "bash", "sh",
    };

    /// <summary>
    /// Returns <c>true</c> if the server's command is ready to launch — either it was
    /// already resolvable, or the user accepted and completed an installation. Returns
    /// <c>false</c> if the user declined, or the installation failed.
    /// </summary>
    /// <param name="config">The MCP server configuration to check.</param>
    /// <param name="xamlRoot">
    /// Host <see cref="XamlRoot"/> used to present dialogs. May be <c>null</c> for
    /// background reconnect paths; in that case the check skips dialogs and just reports
    /// whether the command is resolvable.
    /// </param>
    public static async Task<bool> EnsureCommandAvailableAsync(
        McpServerConfig config,
        XamlRoot? xamlRoot)
    {
        // Only stdio servers spawn subprocesses.
        if (config.TransportType != McpTransportType.Stdio) return true;

        var command = config.Command?.Trim();
        if (string.IsNullOrEmpty(command)) return true;

        // Package runners resolve their real target at invocation time; PATH check on the
        // runner itself is uninformative and only produces false negatives.
        if (KnownRunners.Contains(command)) return true;

        if (await IsCommandOnPathAsync(command)) return true;

        // Command not found. Without a XamlRoot we cannot prompt — treat as failure so
        // callers (e.g. background reconnect) can surface the original transport error.
        if (xamlRoot is null) return false;

        var loader = new ResourceLoader();
        var installDialog = new ContentDialog
        {
            Title = string.Format(loader.GetString("Connect_InstallTitle"), command),
            Content = string.Format(loader.GetString("Connect_InstallMessage"), command),
            PrimaryButtonText = loader.GetString("Connect_InstallPrimary"),
            CloseButtonText = loader.GetString("Connect_InstallCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        var choice = await installDialog.ShowAsync();
        if (choice != ContentDialogResult.Primary) return false;

        return await InstallDotnetToolAsync(config, command, xamlRoot, loader);
    }

    /// <summary>
    /// Returns <c>true</c> when <c>where.exe &lt;command&gt;</c> finds at least one
    /// match on PATH. Any exception or non-zero exit code is treated as "not found".
    /// </summary>
    private static async Task<bool> IsCommandOnPathAsync(string command)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            LoggingService.LogWarning($"[MCP] where.exe check failed for '{command}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Runs <c>dotnet tool install -g &lt;input&gt;</c> where <paramref name="input"/>
    /// is whatever the user typed in the Command field. On success:
    /// <list type="bullet">
    /// <item>If the same string resolves on PATH, returns <c>true</c> unchanged.</item>
    /// <item>Otherwise consults <c>dotnet tool list -g</c> to find the actual tool
    /// command (handles the case where the user entered the package id instead of the
    /// command). If a match is found, <paramref name="config"/>.<c>Command</c> is
    /// rewritten to the real command, a notification informs the user of the correction,
    /// and <c>true</c> is returned. Callers should persist the config after this helper
    /// returns <c>true</c> since <c>Command</c> may have changed.</item>
    /// </list>
    /// On failure (exit code non-zero, or success but no resolvable command anywhere),
    /// an error dialog is shown and <c>false</c> is returned.
    /// </summary>
    /// <remarks>
    /// <c>dotnet</c> CLI output is forced to English via <c>DOTNET_CLI_UI_LANGUAGE=en</c>
    /// and UTF-8 via explicit stream encoding so non-ASCII locale output does not end up
    /// mojibake in the error dialog.
    /// </remarks>
    private static async Task<bool> InstallDotnetToolAsync(
        McpServerConfig config,
        string input,
        XamlRoot xamlRoot,
        ResourceLoader loader)
    {
        LoggingService.LogInfo($"[MCP] Installing dotnet tool: {input}");

        var progressDialog = new ContentDialog
        {
            Title = loader.GetString("Connect_InstallingTitle"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = string.Format(loader.GetString("Connect_InstallingMessage"), input),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new ProgressBar { IsIndeterminate = true },
                },
            },
            XamlRoot = xamlRoot,
        };

        // Fire-and-forget show so we can dismiss when the install completes.
        var showTask = progressDialog.ShowAsync();

        var stdout = string.Empty;
        var stderr = string.Empty;
        var exitCode = -1;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"tool install -g {input}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Force English + UTF-8 CLI output so the error dialog never shows mojibake
            // when the user's system locale is non-English.
            psi.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            psi.Environment["NO_COLOR"] = "1";

            using var proc = Process.Start(psi);
            if (proc is not null)
            {
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                stdout = await stdoutTask;
                stderr = await stderrTask;
                exitCode = proc.ExitCode;
            }
        }
        catch (Exception ex)
        {
            stderr = ex.Message;
            LoggingService.LogError($"[MCP] dotnet tool install failed for '{input}': {ex}");
        }
        finally
        {
            progressDialog.Hide();
        }

        if (exitCode == 0)
        {
            if (await IsCommandOnPathAsync(input))
            {
                LoggingService.LogInfo($"[MCP] dotnet tool installed: {input}");
                return true;
            }

            // Install reported success but our command isn't on PATH. The user likely
            // entered the NuGet package id (e.g. "FieldCure.Mcp.PublicData.Kr") whose
            // actual tool command has a different shape ("fieldcure-mcp-publicdata-kr").
            // Resolve the real command from `dotnet tool list -g` and auto-correct the
            // server config so the user doesn't have to edit Command manually.
            var actualCommand = await ResolveActualCommandAsync(input);
            if (!string.IsNullOrEmpty(actualCommand)
                && !actualCommand.Equals(input, StringComparison.OrdinalIgnoreCase)
                && await IsCommandOnPathAsync(actualCommand))
            {
                LoggingService.LogInfo(
                    $"[MCP] Auto-correcting command for '{config.Name}': '{input}' → '{actualCommand}'");
                config.Command = actualCommand;

                NotificationCenter.Instance.Post(
                    InfoBarSeverity.Informational,
                    loader.GetString("Connect_InstallMismatchTitle"),
                    string.Format(loader.GetString("Connect_InstallMismatchMessage"), input, actualCommand),
                    6000);

                return true;
            }
        }

        // Prefer stderr if present, fall back to stdout for CLI that report to stdout.
        var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
        LoggingService.LogWarning(
            $"[MCP] dotnet tool install did not produce a runnable '{input}' " +
            $"(exit={exitCode}): {detail}");

        var errorDialog = new ContentDialog
        {
            Title = loader.GetString("Connect_InstallFailedTitle"),
            Content = string.Format(loader.GetString("Connect_InstallFailedMessage"), input, detail),
            CloseButtonText = loader.GetString("Connect_InstallClose"),
            XamlRoot = xamlRoot,
        };
        await errorDialog.ShowAsync();
        return false;
    }

    /// <summary>
    /// Looks up the actual tool command corresponding to <paramref name="input"/> in the
    /// global tool catalog. The input can be either a command name already (in which
    /// case it is returned unchanged) or a NuGet package id (in which case we return the
    /// first command declared by that package). Returns <c>null</c> if neither form
    /// matches any installed global tool.
    /// </summary>
    private static async Task<string?> ResolveActualCommandAsync(string input)
    {
        var map = await ExternalDotnetToolUpdater.GetGlobalToolCommandMapAsync();
        if (map.Count == 0) return null;

        // Input is already a known command?
        var asCommand = map.Keys.FirstOrDefault(c => c.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (asCommand is not null) return asCommand;

        // Input looks like a package id — find the command(s) registered by that package.
        var asPackage = map.FirstOrDefault(kv => kv.Value.Equals(input, StringComparison.OrdinalIgnoreCase));
        return asPackage.Key; // null if no match (default KeyValuePair)
    }
}
