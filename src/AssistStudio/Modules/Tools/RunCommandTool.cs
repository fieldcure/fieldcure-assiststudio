using FieldCure.AssistStudio.Models;
using System.Diagnostics;
using System.Text.Json;

namespace AssistStudio.Modules.Tools;

/// <summary>
/// Executes a shell command and returns stdout, stderr, and exit code.
/// </summary>
public class RunCommandTool : IAssistTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const int MaxOutputBytes = 10 * 1024; // 10 KB per stream

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "run_command";

    /// <inheritdoc/>
    public string DisplayName => "Run Command";

    /// <inheritdoc/>
    public string Description => "Executes a shell command and returns the exit code, stdout, and stderr. Requires user confirmation before execution.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "command": { "type": "string", "description": "The shell command to execute" },
            "working_directory": { "type": "string", "description": "Working directory for the command (optional)" }
          },
          "required": ["command"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => true;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var command = parameters.GetProperty("command").GetString()
            ?? throw new ArgumentException("Missing required parameter: command");

        var workingDir = parameters.TryGetProperty("working_directory", out var wdEl)
            ? wdEl.GetString()
            : null;

        if (!string.IsNullOrEmpty(workingDir) && !Directory.Exists(workingDir))
            return JsonSerializer.Serialize(new { error = $"Working directory not found: {workingDir}" });

        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd.exe" : "/bin/bash",
            Arguments = isWindows ? $"/c {command}" : $"-c \"{command.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrEmpty(workingDir))
            psi.WorkingDirectory = workingDir;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start process.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return JsonSerializer.Serialize(new
            {
                exitCode = process.ExitCode,
                stdout = Truncate(stdout, MaxOutputBytes),
                stderr = Truncate(stderr, MaxOutputBytes),
            });
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new { error = $"Command timed out after {Timeout.TotalSeconds} seconds: {command}" });
        }
        catch (Exception ex) when (ex is not ArgumentException)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to execute command: {ex.Message}" });
        }
    }

    #endregion

    private static string Truncate(string text, int maxBytes)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(text) <= maxBytes)
            return text;

        // Trim to fit within byte limit
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var truncated = System.Text.Encoding.UTF8.GetString(bytes, 0, maxBytes);
        return truncated + "\n... (truncated)";
    }
}
