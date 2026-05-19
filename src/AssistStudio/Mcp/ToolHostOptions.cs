using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// Bound configuration for the embedded <c>FieldCure.ToolHost</c> runner.
/// Loaded once from <c>appsettings.json</c> at app startup and consumed by <see cref="DnxHost"/>.
/// </summary>
/// <remarks>
/// Kept as a single-field POCO so a future config knob (an extra source URL, a flag, etc.)
/// has an obvious home without requiring a new types-and-loader refactor. Today the only
/// knob is <see cref="VersionCheckTtlHours"/>; everything else (NuGet sources, the
/// <c>nuget.org</c> fallback) is intentionally a constant — see <see cref="DnxHost"/> for
/// the rationale.
/// </remarks>
public sealed class ToolHostOptions
{
    /// <summary>TTL in hours for <c>CachedWithRefresh</c> version refresh. Default: 24.</summary>
    public int VersionCheckTtlHours { get; init; } = 24;

    /// <summary>
    /// Loads options from an <c>appsettings.json</c> file. Returns defaults when the file
    /// or the <c>"ToolHost"</c> section is missing. Throws on malformed JSON.
    /// </summary>
    /// <param name="path">Absolute path to <c>appsettings.json</c>.</param>
    public static ToolHostOptions LoadFromJson(string path)
    {
        if (!File.Exists(path))
            return new ToolHostOptions();

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        if (!doc.RootElement.TryGetProperty("ToolHost", out var section))
            return new ToolHostOptions();

        var defaults = new ToolHostOptions();
        var ttl = defaults.VersionCheckTtlHours;

        if (section.TryGetProperty("VersionCheckTtlHours", out var ttlEl)
            && ttlEl.ValueKind == JsonValueKind.Number
            && ttlEl.TryGetInt32(out var ttlVal)
            && ttlVal > 0)
        {
            ttl = ttlVal;
        }

        return new ToolHostOptions { VersionCheckTtlHours = ttl };
    }
}
