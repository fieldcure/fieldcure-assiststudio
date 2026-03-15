using FieldCure.AssistStudio.Models;
using System.Text.Json;

namespace AssistStudio.Modules.Tools;

/// <summary>
/// A demo tool that scans a directory and returns a list of files with name, size, and modified date.
/// </summary>
public class ScanDirectoryTool : IAssistTool
{
    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "scan_directory";

    /// <inheritdoc/>
    public string Description => "Scans a directory and returns a list of files with name, size (bytes), and last modified date.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Directory path to scan" },
            "recursive": { "type": "boolean", "description": "Whether to scan subdirectories recursively", "default": false }
          },
          "required": ["path"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => false;

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var path = parameters.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing required parameter: path");

        var recursive = parameters.TryGetProperty("recursive", out var recEl) && recEl.GetBoolean();

        if (!Directory.Exists(path))
            return Task.FromResult(JsonSerializer.Serialize(new { error = $"Directory not found: {path}" }));

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(path, "*", searchOption)
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new
                {
                    name = info.Name,
                    path = info.FullName,
                    size = info.Length,
                    modified = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
                };
            })
            .ToList();

        var result = new { count = files.Count, files };
        return Task.FromResult(JsonSerializer.Serialize(result));
    }

    #endregion
}
