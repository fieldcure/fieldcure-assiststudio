using AssistStudio.Helpers;
using FieldCure.ToolHost;
using FieldCure.ToolHost.Execution;
using FieldCure.ToolHost.Extraction;
using FieldCure.ToolHost.Resolution;
using System.Diagnostics;

namespace AssistStudio.Mcp;

/// <summary>
/// Default <see cref="IDnxHost"/>. Owns a single <see cref="DnxLiteRunner"/> backed by
/// the user's <c>NuGet.Config</c> plus a <see cref="NuGetOrgV3Index"/> fallback added
/// via <see cref="NuGetPackageResolverOptions.AdditionalSources"/>.
/// </summary>
/// <remarks>
/// <para>
/// The fallback exists because fresh Windows installs (MS Store path is the motivating
/// case) ship without a user-level <c>NuGet.Config</c>: <see cref="Settings.LoadDefaultSettings"/>
/// then returns an empty source list and resolution would throw
/// <see cref="InvalidOperationException"/> on the very first launch. Adding
/// <c>nuget.org</c> as an extra source makes the resolver fall through to the public feed
/// whenever the user's configured sources are absent or do not satisfy the request.
/// </para>
/// <para>
/// Earlier drafts of this class kept two runners — a "trusted" one pinned to nuget.org
/// via <see cref="NuGetPackageResolverOptions.RestrictToSource"/> for first-party packages,
/// and a "user-config" one for everything else — to defend against dependency-confusion
/// attacks on corporate feeds. That design has been removed: it forced every new first-party
/// package id into a TrustedNamespaces allow-list (the omission of <c>FieldCure.AssistStudio.</c>
/// silently broke the Runner on fresh PCs, exactly the scenario this whole migration
/// targeted), and the threat it defended against is better handled by NuGet's own
/// Package Source Mapping than by a runtime branch in this host. Anyone who wants the
/// stronger guarantee can configure source mapping in their <c>NuGet.Config</c>.
/// </para>
/// </remarks>
public sealed class DnxHost : IDnxHost
{
    #region Constants

    /// <summary>Public nuget.org v3 service index, added as an <c>AdditionalSources</c> fallback.</summary>
    private const string NuGetOrgV3Index = "https://api.nuget.org/v3/index.json";

    #endregion

    #region Fields

    private readonly ToolHostOptions _options;
    private DotnetEnvironment? _environment;
    private DnxLiteRunner? _runner;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    #endregion

    #region Constructor

    /// <summary>Constructs a host bound to the supplied <see cref="ToolHostOptions"/>.</summary>
    public DnxHost(ToolHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    #endregion

    #region IDnxHost

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            // Mark as "attempted" up front so a failure won't be retried on every
            // subsequent StartAsync call — DotnetEnvironment.DetectAsync spawns dotnet
            // subprocesses and we don't want a hot loop.
            _initialized = true;

            _environment = await DotnetEnvironment.DetectAsync(ct).ConfigureAwait(false);
            LoggingService.LogInfo(
                $"[DnxHost] dotnet detected at {_environment.DotnetMuxerPath}; " +
                $"SDKs=[{string.Join(", ", _environment.InstalledSdks)}]; " +
                $"runtimes=[{string.Join(", ", _environment.InstalledRuntimes)}]; " +
                $"RID={_environment.RuntimeIdentifier}");

            var resolverOptions = new NuGetPackageResolverOptions
            {
                AdditionalSources = [NuGetOrgV3Index],
                RefreshTtl = TimeSpan.FromHours(_options.VersionCheckTtlHours),
                IgnoreFailedSources = true,
            };

            _runner = new DnxLiteRunner(
                _environment,
                new NuGetPackageResolver(resolverOptions, new ToolCacheIndexStore()),
                new NuGetToolExtractor(_environment),
                new ToolLauncher());
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Returns <see langword="true"/> when <see cref="InitializeAsync"/> completed and a
    /// runner is ready to launch tools. <see langword="false"/> until initialization runs
    /// (or after it fails — typically because the .NET runtime is missing on this PC).
    /// </summary>
    public bool IsReady => _runner is not null;

    /// <inheritdoc />
    public Task<Process> StartAsync(
        string packageId,
        string? versionRange,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? environmentVariables = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentNullException.ThrowIfNull(args);

        if (_runner is null)
        {
            // Two reasons we can be here: (1) InitializeAsync was never awaited (programming
            // error); (2) InitializeAsync was awaited but DotnetEnvironment.DetectAsync threw
            // because no .NET runtime is installed (the common fresh-PC case). Both surface
            // the same user-visible symptom — MCP tools can't launch — so we use one short
            // message that lands well on the per-card status text. The longer "Install from
            // dotnet.microsoft.com" guidance lives on the MainWindow InfoBar and the
            // ConnectPage notice, where the user can also click an install link.
            throw new InvalidOperationException("Requires .NET 10 Desktop Runtime");
        }

        var request = new ToolInvocationRequest
        {
            PackageId = packageId,
            ToolArguments = args,
            Policy = ToolVersionPolicy.CachedWithRefresh,
            VersionConstraint = versionRange,
            AdditionalEnvironment = environmentVariables,
        };

        return _runner.StartAsync(request, ct);
    }

    #endregion
}
