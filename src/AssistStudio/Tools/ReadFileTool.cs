using FieldCure.AssistStudio.Models;
using FieldCure.DocumentParsers;
using System.Text;
using System.Text.Json;

namespace AssistStudio.Tools;

/// <summary>
/// Reads and returns the contents of a file at the specified path.
/// For document formats (PDF, DOCX, XLSX, PPTX, HWPX), extracts text via DocumentParsers.
/// For plain text files, reads with the specified encoding.
/// </summary>
public class ReadFileTool : IAssistTool
{
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB (documents can be larger than plain text)

    #region IAssistTool Implementation

    /// <inheritdoc/>
    public string Name => "read_file";

    /// <inheritdoc/>
    public string DisplayName => "Read File";

    /// <inheritdoc/>
    public string Description =>
        "Reads file content and returns extracted text. " +
        "Supports plain text files (TXT, MD, CSV, JSON, XML, source code, etc.) " +
        "and document formats (PDF, DOCX, XLSX, PPTX, HWPX). " +
        "For documents, text is automatically extracted from the binary format.";

    /// <inheritdoc/>
    public string ParameterSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute path to the file to read" },
            "encoding": { "type": "string", "description": "Text encoding for plain text files (default: utf-8)", "default": "utf-8" }
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

        try
        {
            // Try document parser first (PDF, DOCX, XLSX, PPTX, HWPX)
            var ext = fileInfo.Extension;
            var parser = DocumentParserFactory.GetParser(ext);
            if (parser is not null)
            {
                var data = await File.ReadAllBytesAsync(path, ct);
                var content = parser.ExtractText(data);
                return JsonSerializer.Serialize(new { path, size = fileInfo.Length, format = ext.TrimStart('.').ToUpperInvariant(), content });
            }

            // Fall back to plain text
            var encodingName = parameters.TryGetProperty("encoding", out var encEl) ? encEl.GetString() : null;
            var encoding = GetEncoding(encodingName);
            var textContent = await File.ReadAllTextAsync(path, encoding, ct);
            return JsonSerializer.Serialize(new { path, size = fileInfo.Length, content = textContent });
        }
        catch (UnauthorizedAccessException)
        {
            return JsonSerializer.Serialize(new { error = $"Access denied: {path}" });
        }
        catch (IOException ex)
        {
            return JsonSerializer.Serialize(new { error = $"IO error reading {path}: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = $"Failed to parse {path}: {ex.Message}" });
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
