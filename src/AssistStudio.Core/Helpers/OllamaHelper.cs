using System.Diagnostics;

namespace FieldCure.AssistStudio.Helpers;

/// <summary>
/// Utility methods for detecting, starting, and managing the local Ollama installation.
/// </summary>
public static class OllamaHelper
{
    #region Constants

    /// <summary>The default local Ollama server URL.</summary>
    private const string OllamaUrl = "http://localhost:11434";

    /// <summary>The URL where Ollama can be downloaded.</summary>
    private const string InstallUrl = "https://ollama.com/download";

    #endregion

    #region Public Methods

    /// <summary>
    /// Returns the URL where Ollama can be downloaded.
    /// </summary>
    public static string GetInstallUrl() => InstallUrl;

    /// <summary>
    /// Checks whether Ollama is installed on the local machine by inspecting known paths and the system PATH.
    /// </summary>
    public static bool IsOllamaInstalled()
    {
        // Check common install locations on Windows
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var ollamaPath = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        if (File.Exists(ollamaPath))
            return true;

        // Check if ollama is in PATH
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "ollama",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            proc.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            return false;
        }
    }

    /// <summary>
    /// Checks whether the Ollama server is currently running and responsive.
    /// </summary>
    public static async Task<bool> IsOllamaRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync(OllamaUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            return false;
        }
    }

    /// <summary>
    /// Attempts to start the Ollama server process and waits for it to become responsive.
    /// </summary>
    public static async Task<bool> StartOllamaAsync(CancellationToken ct = default)
    {
        try
        {
            // Try known install path first
            var exePath = FindOllamaExe();
            var psi = new ProcessStartInfo
            {
                FileName = exePath ?? "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            Process.Start(psi);

            // Wait for healthcheck (poll up to 15 seconds)
            for (var i = 0; i < 30; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(500, ct);

                if (await IsOllamaRunningAsync(ct))
                    return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException(ex);
            return false;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>Finds the Ollama executable in common install locations.</summary>
    private static string? FindOllamaExe()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        return File.Exists(path) ? path : null;
    }

    #endregion
}
