using FieldCure.AssistStudio.Models;
using System.Text.Json;

namespace AssistStudio.Modules.Tools;

/// <summary>
/// Writes content to a file at the specified path, with overwrite protection.
/// </summary>
public class WriteFileTool : IAssistTool
{
    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "write_file";

    /// <inheritdoc/>
    public string DisplayName => "Write File";

    /// <inheritdoc/>
    public string Description => "Writes text content to a file. Creates parent directories if needed. By default, refuses to overwrite existing files unless overwrite is set to true.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute path to the file to write" },
            "content": { "type": "string", "description": "Text content to write to the file" },
            "overwrite": { "type": "boolean", "description": "Allow overwriting an existing file (default: false)", "default": false }
          },
          "required": ["path", "content"]
        }
        """;

    /// <inheritdoc/>
    public bool RequiresConfirmation => true;

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(JsonElement parameters, CancellationToken ct = default)
    {
        var path = parameters.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing required parameter: path");
        var content = parameters.GetProperty("content").GetString()
            ?? throw new ArgumentException("Missing required parameter: content");
        var overwrite = parameters.TryGetProperty("overwrite", out var owEl) && owEl.GetBoolean();

        if (!overwrite && File.Exists(path))
            return JsonSerializer.Serialize(new { error = $"File already exists: {path}. Set overwrite to true to replace." });

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, content, ct);

            return JsonSerializer.Serialize(new { path, bytesWritten = new FileInfo(path).Length });
        }
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = $"Access denied: {path}" });
        }
        catch (IOException ex)
        {
            return JsonSerializer.Serialize(new { error = $"IO error writing {path}: {ex.Message}" });
        }
    }

    #endregion
}
