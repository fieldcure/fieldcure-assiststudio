using System.Diagnostics;

namespace AssistStudio.Mcp;

/// <summary>
/// Embedded NuGet tool runner. Replaces the legacy <c>dnx</c>-on-PATH invocation with an
/// in-process <see cref="FieldCure.ToolHost.DnxLiteRunner"/> that resolves, downloads, and
/// launches .NET tool packages without requiring the .NET 10 SDK.
/// </summary>
/// <remarks>
/// Implementations honor the user's <c>NuGet.Config</c> as the primary source list and add
/// <c>nuget.org</c> as an <c>AdditionalSources</c> fallback so first launches on fresh PCs
/// — where no user-level <c>NuGet.Config</c> exists yet — can still resolve packages.
/// </remarks>
public interface IDnxHost
{
    /// <summary>
    /// One-time async initialization. Detects the host <c>dotnet</c> environment and caches the
    /// result for the process lifetime. Must complete before <see cref="StartAsync"/> is called.
    /// Throws when the <c>dotnet</c> muxer is not on <c>PATH</c> / <c>DOTNET_ROOT</c>.
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves, downloads if needed, and launches a tool. Returns a started <see cref="Process"/>
    /// with <c>stdin</c>/<c>stdout</c>/<c>stderr</c> redirected — the caller owns lifetime and stream IO.
    /// </summary>
    /// <param name="packageId">NuGet package id of the tool.</param>
    /// <param name="versionRange">Optional NuGet version range (e.g. <c>"2.*"</c>); null → policy-controlled latest.</param>
    /// <param name="args">Arguments forwarded to the tool entry point.</param>
    /// <param name="environmentVariables">Optional env vars to set on the child process; null value removes a var.</param>
    /// <param name="ct">Cancellation honored during resolve/download; process launch is uncancellable once started.</param>
    Task<Process> StartAsync(
        string packageId,
        string? versionRange,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken ct = default);
}
