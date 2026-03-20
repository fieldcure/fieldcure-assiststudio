using System.IO;
using System.Linq;

namespace AssistStudio.Helpers;

/// <summary>
/// Provides methods for formatting file paths for display in UI elements.
/// </summary>
public static class PathFormatter
{
    private const string Ellipsis = "...";
    private static readonly char DirectorySeparator = Path.DirectorySeparatorChar;

    /// <summary>
    /// Formats a file path for display in MRU (Most Recently Used) lists.
    /// </summary>
    /// <param name="path">The full file path to format.</param>
    /// <param name="maxLength">The maximum length of the formatted path. Default is 60.</param>
    /// <returns>A formatted path suitable for display.</returns>
    public static string FormatForMRU(string path, int maxLength = 60)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        // If path is already short enough, return as is
        if (path.Length <= maxLength)
            return path;

        try
        {
            var fileName = Path.GetFileName(path);
            var directory = Path.GetDirectoryName(path) ?? string.Empty;

            // If filename alone is too long, truncate it
            if (fileName.Length >= maxLength - Ellipsis.Length)
            {
                var startIndex = fileName.Length - (maxLength - Ellipsis.Length);
                return string.Concat(Ellipsis, fileName.AsSpan(startIndex));
            }

            // Calculate how much space we have for the directory
            var availableSpace = maxLength - fileName.Length - 4; // 4 for "\..." or "...\"

            if (availableSpace <= 0)
            {
                return string.Concat(Ellipsis, DirectorySeparator.ToString(), fileName);
            }

            // Try to keep the drive letter and some parent folders
            var parts = directory.Split(DirectorySeparator);

            if (parts.Length > 0)
            {
                // Build compact path
                var root = parts[0];
                var isRootDrive = root.EndsWith(':') || root == string.Empty;

                // Calculate which folders to keep from the end
                var startIndex = isRootDrive ? 1 : 0;
                var totalLength = root.Length + (isRootDrive ? 1 : 0); // +1 for separator

                // Find how many folders from the end we can include
                var foldersToInclude = 0;
                for (var i = parts.Length - 1; i >= startIndex; i--)
                {
                    var folderLength = parts[i].Length + 1; // +1 for separator
                    if (totalLength + folderLength + Ellipsis.Length + 1 <= availableSpace)
                    {
                        totalLength += folderLength;
                        foldersToInclude++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (foldersToInclude > 0)
                {
                    var endFolders = string.Join(DirectorySeparator,
                        parts.Skip(parts.Length - foldersToInclude).Take(foldersToInclude));

                    return isRootDrive
                        ? string.Concat(root, DirectorySeparator.ToString(), Ellipsis,
                            DirectorySeparator.ToString(), endFolders, DirectorySeparator.ToString(), fileName)
                        : string.Concat(Ellipsis, DirectorySeparator.ToString(), endFolders,
                            DirectorySeparator.ToString(), fileName);
                }

                return string.Concat(root, DirectorySeparator.ToString(), Ellipsis,
                    DirectorySeparator.ToString(), fileName);
            }
        }
        catch
        {
            // If any error occurs, fall back to simple truncation
        }

        // Fallback: simple truncation from the beginning
        var truncateStart = path.Length - (maxLength - Ellipsis.Length);
        return string.Concat(Ellipsis, path.AsSpan(truncateStart));
    }

    /// <summary>
    /// Compacts a file path by showing only the most significant parts.
    /// Shows drive, first folder, last folder(s), and filename.
    /// </summary>
    /// <param name="path">The full file path to compact.</param>
    /// <param name="maxLength">The maximum length of the compacted path. Default is 50.</param>
    /// <returns>A compacted version of the path.</returns>
    public static string CompactPath(string path, int maxLength = 50)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        if (path.Length <= maxLength)
            return path;

        try
        {
            var fileName = Path.GetFileName(path);
            var directory = Path.GetDirectoryName(path) ?? string.Empty;

            // If the filename is too long, just truncate it
            if (fileName.Length > maxLength - 10) // Leave some room for directory indication
            {
                var extension = Path.GetExtension(fileName);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var nameLength = maxLength - extension.Length - Ellipsis.Length;

                if (nameLength > 0 && nameWithoutExt.Length > nameLength)
                {
                    return string.Concat(
                        nameWithoutExt.AsSpan(0, nameLength),
                        Ellipsis,
                        extension
                    );
                }
            }

            var parts = directory.Split(DirectorySeparator);

            if (parts.Length <= 2)
            {
                // Short path, just use ellipsis in the middle
                return FormatForMRU(path, maxLength);
            }

            // Build a compact representation: Drive:\First\...\Last\File.ext
            var root = parts[0];
            var separator = DirectorySeparator.ToString();
            var hasRoot = !string.IsNullOrEmpty(root);

            // Get first meaningful folder
            var firstFolder = parts.Length > 1 ? parts[1] : string.Empty;
            var lastFolder = parts.Length > 2 ? parts[^1] : string.Empty;

            var compactPath = hasRoot
                ? string.Concat(root, separator, firstFolder, separator, Ellipsis, separator, lastFolder, separator, fileName)
                : string.Concat(firstFolder, separator, Ellipsis, separator, lastFolder, separator, fileName);

            // If still too long, use the simple format
            return compactPath.Length > maxLength
                ? FormatForMRU(path, maxLength)
                : compactPath;
        }
        catch
        {
            // Fallback to simple truncation
            var truncateStart = path.Length - (maxLength - Ellipsis.Length);
            return string.Concat(Ellipsis, path.AsSpan(truncateStart));
        }
    }

    /// <summary>
    /// Formats a path for menu display with an index number.
    /// </summary>
    /// <param name="index">The menu item index (1-based).</param>rkxdl
    /// <param name="path">The full file path.</param>
    /// <param name="maxLength">The maximum length for the path portion. Default is 50.</param>
    /// <returns>A formatted string suitable for menu display.</returns>
    public static string FormatForMenu(int index, string path, int maxLength = 50)
    {
        var formattedPath = CompactPath(path, maxLength);
        return string.Concat(index.ToString(), ". ", formattedPath);
    }
}
