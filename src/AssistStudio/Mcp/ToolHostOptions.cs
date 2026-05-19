using System.Text.Json;

namespace AssistStudio.Mcp;

/// <summary>
/// Bound configuration for the embedded <c>FieldCure.ToolHost</c> runner.
/// Loaded once from <c>appsettings.json</c> at app startup and consumed by <see cref="DnxHost"/>.
/// </summary>
/// <remarks>
/// Field semantics:
/// <list type="bullet">
/// <item><description><see cref="TrustedNamespaces"/> — package id prefixes (case-insensitive) that route
/// through the "trusted" resolver, which restricts NuGet resolution to <see cref="TrustedSource"/> only,
/// ignoring the user's <c>NuGet.Config</c> and private feeds.</description></item>
/// <item><description><see cref="TrustedSource"/> — single NuGet source URI used for trusted packages.</description></item>
/// <item><description><see cref="VersionCheckTtlHours"/> — TTL for the <c>CachedWithRefresh</c> policy.
/// Maps to <c>NuGetPackageResolverOptions.RefreshTtl</c>.</description></item>
/// </list>
/// </remarks>
public sealed class ToolHostOptions
{
    /// <summary>Package id prefixes that should be routed to the trusted resolver. Default: FieldCure / RedoxNet MCP namespaces.</summary>
    public IReadOnlyList<string> TrustedNamespaces { get; init; } = ["FieldCure.Mcp.", "RedoxNet.Mcp."];

    /// <summary>Single NuGet source URI used for trusted packages. Default: nuget.org v3 index.</summary>
    public string TrustedSource { get; init; } = "https://api.nuget.org/v3/index.json";

    /// <summary>TTL in hours for <c>CachedWithRefresh</c> version refresh. Default: 24.</summary>
    public int VersionCheckTtlHours { get; init; } = 24;

    /// <summary>
    /// Loads options from an <c>appsettings.json</c> file. Falls back to defaults if the file or
    /// <c>"ToolHost"</c> section is missing. Throws on malformed JSON.
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

        IReadOnlyList<string> trustedNamespaces = defaults.TrustedNamespaces;
        if (section.TryGetProperty("TrustedNamespaces", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>(arr.GetArrayLength());
            foreach (var item in arr.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
                    list.Add(s);
            }
            if (list.Count > 0)
                trustedNamespaces = list;
        }

        var trustedSource = defaults.TrustedSource;
        if (section.TryGetProperty("TrustedSource", out var srcEl)
            && srcEl.ValueKind == JsonValueKind.String
            && srcEl.GetString() is { Length: > 0 } src)
        {
            trustedSource = src;
        }

        var ttl = defaults.VersionCheckTtlHours;
        if (section.TryGetProperty("VersionCheckTtlHours", out var ttlEl)
            && ttlEl.ValueKind == JsonValueKind.Number
            && ttlEl.TryGetInt32(out var ttlVal)
            && ttlVal > 0)
        {
            ttl = ttlVal;
        }

        return new ToolHostOptions
        {
            TrustedNamespaces = trustedNamespaces,
            TrustedSource = trustedSource,
            VersionCheckTtlHours = ttl,
        };
    }

    /// <summary>Returns <see langword="true"/> if <paramref name="packageId"/> starts with any of <see cref="TrustedNamespaces"/> (case-insensitive).</summary>
    public bool IsTrusted(string packageId)
    {
        foreach (var prefix in TrustedNamespaces)
        {
            if (packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
