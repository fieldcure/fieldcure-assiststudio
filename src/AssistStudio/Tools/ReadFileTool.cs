using FieldCure.AssistStudio.Models;
using System.Text;
using System.Text.Json;

namespace AssistStudio.Tools;

/// <summary>
/// Reads and returns the contents of a file at the specified path.
/// </summary>
public class ReadFileTool : IAssistTool
{
    private const long MaxFileSizeBytes = 1 * 1024 * 1024; // 1 MB

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "read_file";

    /// <inheritdoc/>
    public string DisplayName => "Read File";

    /// <inheritdoc/>
    public string Description => "Reads the contents of a file and returns it as text. Supports UTF-8 encoding by default.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute path to the file to read" },
            "encoding": { "type": "string", "description": "Text encoding (default: utf-8)", "default": "utf-8" }
          },
          "required": ["path"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var path = parameters.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing required parameter: path");

        if (!File.Exists(path))
            return JsonSerializer.Serialize(new { error = $"File not found: {path}" });

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length > MaxFileSizeBytes)
            return JsonSerializer.Serialize(new { error = $"File too large ({fileInfo.Length:N0} bytes). Maximum allowed: {MaxFileSizeBytes:N0} bytes." });

        var encodingName = parameters.TryGetProperty("encoding", out var encEl) ? encEl.GetString() : null;
        var encoding = GetEncoding(encodingName);

        try
        {
            var content = await File.ReadAllTextAsync(path, encoding, ct);
            return JsonSerializer.Serialize(new { path, size = fileInfo.Length, content });
        }
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = $"Access denied: {path}" });
        }
        catch (IOException ex)
        {
            return JsonSerializer.Serialize(new { error = $"IO error reading {path}: {ex.Message}" });
        }
    }

    #endregion

    private static Encoding GetEncoding(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Equals("utf-8", StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8;

        try { return Encoding.GetEncoding(name); }
        catch { return Encoding.UTF8; }
    }
}
