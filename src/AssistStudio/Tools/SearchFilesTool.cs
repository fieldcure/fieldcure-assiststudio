using FieldCure.AssistStudio.Models;
using System.Text.Json;

namespace AssistStudio.Tools;

/// <summary>
/// Searches a directory for files matching a glob pattern and returns file metadata.
/// </summary>
public class SearchFilesTool : IAssistTool
{
    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "search_files";

    /// <inheritdoc/>
    public string DisplayName => "Search Files";

    /// <inheritdoc/>
    public string Description => "Searches a directory for files matching a glob pattern and returns a list with name, path, size, and last modified date.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Directory path to search" },
            "pattern": { "type": "string", "description": "Glob pattern to match files (default: *)", "default": "*" },
            "recursive": { "type": "boolean", "description": "Whether to search subdirectories recursively", "default": false }
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

        var pattern = parameters.TryGetProperty("pattern", out var patEl)
            ? patEl.GetString() ?? "*"
            : "*";

        var recursive = parameters.TryGetProperty("recursive", out var recEl) && recEl.GetBoolean();

        if (!Directory.Exists(path))
            return JsonSerializer.Serialize(new { error = $"Directory not found: {path}" });

        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        // Run file enumeration on a thread-pool thread to avoid blocking the UI thread.
        var files = await Task.Run(() =>
        {
            var results = new List<object>();
            try
            {
                foreach (var info in new DirectoryInfo(path).EnumerateFiles(pattern, enumOptions))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        results.Add(new
                        {
                            name = info.Name,
                            path = info.FullName,
                            size = info.Length,
                            modified = info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
                        });
                    }
                    catch (UnauthorizedAccessException) { }
                    catch (IOException) { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            return results;
        }, ct);

        var result = new { count = files.Count, files };
        return JsonSerializer.Serialize(result);
    }

    #endregion
}
