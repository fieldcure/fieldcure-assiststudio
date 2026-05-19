using AssistStudio.Helpers;
using FieldCure.ToolHost;
using FieldCure.ToolHost.Execution;
using FieldCure.ToolHost.Extraction;
using FieldCure.ToolHost.Resolution;
using System.Diagnostics;

namespace AssistStudio.Mcp;

/// <summary>
/// Default <see cref="IDnxHost"/>. Owns two <see cref="DnxLiteRunner"/> instances that share the
/// detected <see cref="DotnetEnvironment"/> but differ in NuGet source policy:
/// <list type="bullet">
/// <item><description><c>_trustedRunner</c> — <see cref="NuGetPackageResolverOptions.RestrictToSource"/>
/// pinned to <see cref="ToolHostOptions.TrustedSource"/>; ignores user feeds entirely.</description></item>
/// <item><description><c>_userConfigRunner</c> — honors the user's <c>NuGet.Config</c> as-is, for
/// packages outside <see cref="ToolHostOptions.TrustedNamespaces"/>.</description></item>
/// </list>
/// Both share the 24h (default) <c>RefreshTtl</c> from <see cref="ToolHostOptions.VersionCheckTtlHours"/>.
/// </summary>
public sealed class DnxHost : IDnxHost
{
    #region Fields

    private readonly ToolHostOptions _options;
    private DotnetEnvironment? _environment;
    private DnxLiteRunner? _trustedRunner;
    private DnxLiteRunner? _userConfigRunner;
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

            _environment = await DotnetEnvironment.DetectAsync(ct).ConfigureAwait(false);
            LoggingService.LogInfo(
                $"[DnxHost] dotnet detected at {_environment.DotnetMuxerPath}; " +
                $"SDKs=[{string.Join(", ", _environment.InstalledSdks)}]; " +
                $"runtimes=[{string.Join(", ", _environment.InstalledRuntimes)}]; " +
                $"RID={_environment.RuntimeIdentifier}");

            var refreshTtl = TimeSpan.FromHours(_options.VersionCheckTtlHours);
            var indexStore = new ToolCacheIndexStore();

            var trustedOptions = new NuGetPackageResolverOptions
            {
                RestrictToSource = _options.TrustedSource,
                RefreshTtl = refreshTtl,
                IgnoreFailedSources = false,
            };
            _trustedRunner = new DnxLiteRunner(
                _environment,
                new NuGetPackageResolver(trustedOptions, indexStore),
                new NuGetToolExtractor(_environment),
                new ToolLauncher());

            var userOptions = new NuGetPackageResolverOptions
            {
                RefreshTtl = refreshTtl,
                IgnoreFailedSources = true,
            };
            _userConfigRunner = new DnxLiteRunner(
                _environment,
                new NuGetPackageResolver(userOptions, indexStore),
                new NuGetToolExtractor(_environment),
                new ToolLauncher());

            _initialized = true;
        }
        finally
        {
            _initGate.Release();
        }
    }

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

        if (!_initialized || _trustedRunner is null || _userConfigRunner is null)
        {
            throw new InvalidOperationException(
                $"{nameof(DnxHost)}.{nameof(InitializeAsync)} must complete before {nameof(StartAsync)} is called.");
        }

        var runner = _options.IsTrusted(packageId) ? _trustedRunner : _userConfigRunner;

        var request = new ToolInvocationRequest
        {
            PackageId = packageId,
            ToolArguments = args,
            Policy = ToolVersionPolicy.CachedWithRefresh,
            VersionConstraint = versionRange,
            AdditionalEnvironment = environmentVariables,
        };

        return runner.StartAsync(request, ct);
    }

    #endregion
}
