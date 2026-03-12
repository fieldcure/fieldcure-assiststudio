using System.Diagnostics;

namespace FluentView.AI.Helpers;

public static class OllamaHelper
{
    private const string OllamaUrl = "http://localhost:11434";
    private const string InstallUrl = "https://ollama.com/download";

    public static string GetInstallUrl() => InstallUrl;

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
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsOllamaRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync(OllamaUrl, ct);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

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
        catch
        {
            return false;
        }
    }

    private static string? FindOllamaExe()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var path = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
        return File.Exists(path) ? path : null;
    }
}
