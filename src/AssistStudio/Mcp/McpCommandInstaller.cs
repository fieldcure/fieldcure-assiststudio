using AssistStudio.Helpers;
using FieldCure.AssistStudio.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Diagnostics;
using System.Text;

namespace AssistStudio.Mcp;

/// <summary>
/// Pre-flight check for MCP stdio server commands. If the configured command is not
/// resolvable on PATH (and is not a known package runner such as npx/uvx), offers to
/// install it as a .NET global tool via <c>dotnet tool install -g</c>. The install
/// dialog asks for the NuGet package id separately — the server's <c>Command</c> field
/// always holds the tool command name, never the package id.
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
        "npx", "uvx", "dnx", "node", "python", "python3", "docker",
        "cmd", "powershell", "pwsh", "bash", "sh",
    };

    /// <summary>
    /// Returns <c>true</c> if the server's command is ready to launch — either it was
    /// already resolvable, or the user accepted and completed an installation. Returns
    /// <c>false</c> if the user declined, the installation failed, or the install
    /// succeeded but did not produce the expected command on PATH (wrong package id).
    /// </summary>
    /// <param name="config">
    /// The MCP server configuration to check. Its <see cref="McpServerConfig.Command"/>
    /// is treated as the tool command name (never a package id) and is never mutated
    /// by this helper.
    /// </param>
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

        // Build the install prompt. The dialog carries a TextBox so the user can supply
        // the NuGet package id, which often differs from the tool command name (e.g.
        // command `fieldcure-mcp-publicdata-kr` vs package `FieldCure.Mcp.PublicData.Kr`).
        // We pre-seed it with the entered command as a best-effort default; user can
        // overwrite before clicking Install.
        var packageBox = new TextBox
        {
            Text = command,
            PlaceholderText = loader.GetString("Connect_InstallPackagePlaceholder"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var installDialog = new ContentDialog
        {
            Title = string.Format(loader.GetString("Connect_InstallTitle"), command),
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = loader.GetString("Connect_InstallPackagePrompt"),
                        TextWrapping = TextWrapping.Wrap,
                    },
                    packageBox,
                },
            },
            PrimaryButtonText = loader.GetString("Connect_InstallPrimary"),
            CloseButtonText = loader.GetString("Connect_InstallCancel"),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot,
        };

        var choice = await installDialog.ShowAsync();
        if (choice != ContentDialogResult.Primary) return false;

        var packageId = packageBox.Text?.Trim();
        if (string.IsNullOrEmpty(packageId)) return false;

        return await InstallDotnetToolAsync(command, packageId, xamlRoot, loader);
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
    /// Runs <c>dotnet tool install -g &lt;packageId&gt;</c> and verifies that
    /// <paramref name="expectedCommand"/> becomes resolvable afterwards. The command
    /// and package id are intentionally separate: the server config's <c>Command</c>
    /// always names the tool command, while the package id is entered at install time.
    /// </summary>
    /// <remarks>
    /// <c>dotnet</c> CLI output is forced to English via <c>DOTNET_CLI_UI_LANGUAGE=en</c>
    /// and UTF-8 via explicit stream encoding so non-ASCII locale output does not end up
    /// mojibake in the error dialog.
    /// </remarks>
    private static async Task<bool> InstallDotnetToolAsync(
        string expectedCommand,
        string packageId,
        XamlRoot xamlRoot,
        ResourceLoader loader)
    {
        LoggingService.LogInfo(
            $"[MCP] Installing dotnet tool: package='{packageId}', expecting command='{expectedCommand}'");

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
                        Text = string.Format(loader.GetString("Connect_InstallingMessage"), packageId),
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
                Arguments = $"tool install -g {packageId}",
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
            LoggingService.LogError($"[MCP] dotnet tool install failed for '{packageId}': {ex}");
        }
        finally
        {
            progressDialog.Hide();
        }

        if (exitCode == 0 && await IsCommandOnPathAsync(expectedCommand))
        {
            LoggingService.LogInfo(
                $"[MCP] dotnet tool installed: package='{packageId}', command='{expectedCommand}'");
            return true;
        }

        // Two failure shapes, different messaging for the user:
        //   (a) install failed outright (exit != 0) → show stderr detail
        //   (b) install succeeded but the expected command still isn't on PATH
        //       → the package id the user entered is wrong for this command
        var detail = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
        LoggingService.LogWarning(
            $"[MCP] dotnet tool install did not produce a runnable '{expectedCommand}' " +
            $"(package='{packageId}', exit={exitCode}): {detail}");

        string title;
        string message;
        if (exitCode == 0)
        {
            title = loader.GetString("Connect_InstallWrongPackageTitle");
            message = string.Format(
                loader.GetString("Connect_InstallWrongPackageMessage"),
                expectedCommand,
                packageId);
        }
        else
        {
            title = loader.GetString("Connect_InstallFailedTitle");
            message = string.Format(
                loader.GetString("Connect_InstallFailedMessage"),
                packageId,
                detail);
        }

        var errorDialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = loader.GetString("Connect_InstallClose"),
            XamlRoot = xamlRoot,
        };
        await errorDialog.ShowAsync();
        return false;
    }
}
