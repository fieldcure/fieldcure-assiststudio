using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
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

        return await InstallDotnetToolAsync(command, xamlRoot, loader);
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
    /// Runs <c>dotnet tool install -g &lt;command&gt;</c> and reports success via a
    /// post-install PATH verification. The dialog shown here is a simple indeterminate
    /// progress; success/failure is communicated via an error dialog on failure or by
    /// returning <c>true</c> on success (caller continues the connection attempt).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The heuristic passes the command name as the NuGet package ID. This works for
    /// packages that follow the "package id == tool command" convention. For packages
    /// where they differ (e.g. <c>FieldCure.Mcp.PublicData.Kr</c> has command
    /// <c>fieldcure-mcp-publicdata-kr</c>), the user can enter the package ID and this
    /// helper detects that the install succeeded but produced a different command name,
    /// and surfaces a "mismatch" dialog pointing at the actual command.
    /// </para>
    /// <para>
    /// <c>dotnet</c> CLI output is forced to English via <c>DOTNET_CLI_UI_LANGUAGE=en</c>
    /// and UTF-8 via explicit stream encoding so we can reliably parse the "You can
    /// invoke the tool…" line and so non-ASCII locale output does not end up mojibake
    /// in the error dialog.
    /// </para>
    /// </remarks>
    private static async Task<bool> InstallDotnetToolAsync(
        string command,
        XamlRoot xamlRoot,
        ResourceLoader loader)
    {
        LoggingService.LogInfo($"[MCP] Installing dotnet tool: {command}");

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
                        Text = string.Format(loader.GetString("Connect_InstallingMessage"), command),
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
                Arguments = $"tool install -g {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Force English + UTF-8 CLI output so we can parse reliably and the error
            // dialog never shows CP949/CP1252 mojibake when the user's system locale
            // is non-English.
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
            LoggingService.LogError($"[MCP] dotnet tool install failed for '{command}': {ex}");
        }
        finally
        {
            progressDialog.Hide();
        }

        if (exitCode == 0)
        {
            if (await IsCommandOnPathAsync(command))
            {
                LoggingService.LogInfo($"[MCP] dotnet tool installed: {command}");
                return true;
            }

            // Install reported success but our command isn't on PATH. This usually means
            // the user entered the NuGet package id, and the actual tool command differs
            // (common for dotted package names). Parse the "You can invoke the tool…"
            // line and tell the user which command to configure.
            var actualCommand = ExtractInstalledCommand(stdout);
            if (!string.IsNullOrEmpty(actualCommand)
                && !actualCommand.Equals(command, StringComparison.OrdinalIgnoreCase)
                && await IsCommandOnPathAsync(actualCommand))
            {
                LoggingService.LogWarning(
                    $"[MCP] Install succeeded but command differs: entered='{command}', actual='{actualCommand}'");

                var mismatchDialog = new ContentDialog
                {
                    Title = loader.GetString("Connect_InstallMismatchTitle"),
                    Content = string.Format(
                        loader.GetString("Connect_InstallMismatchMessage"),
                        command,
                        actualCommand),
                    CloseButtonText = loader.GetString("Connect_InstallClose"),
                    XamlRoot = xamlRoot,
                };
                await mismatchDialog.ShowAsync();
                return false;
            }
        }

        // Prefer stderr if present, fall back to stdout for CLI that report to stdout.
        var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
        LoggingService.LogWarning(
            $"[MCP] dotnet tool install did not produce a runnable '{command}' " +
            $"(exit={exitCode}): {detail}");

        var errorDialog = new ContentDialog
        {
            Title = loader.GetString("Connect_InstallFailedTitle"),
            Content = string.Format(loader.GetString("Connect_InstallFailedMessage"), command, detail),
            CloseButtonText = loader.GetString("Connect_InstallClose"),
            XamlRoot = xamlRoot,
        };
        await errorDialog.ShowAsync();
        return false;
    }

    /// <summary>
    /// Pulls the tool command name out of <c>dotnet tool install</c>'s stdout.
    /// Relies on the English output "You can invoke the tool using the following
    /// command: &lt;cmd&gt;" (forced via <c>DOTNET_CLI_UI_LANGUAGE=en</c>).
    /// </summary>
    private static string? ExtractInstalledCommand(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout)) return null;
        var match = Regex.Match(
            stdout,
            @"following command:\s*(?<cmd>\S+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["cmd"].Value : null;
    }
}
