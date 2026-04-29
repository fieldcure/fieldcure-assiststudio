using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Core.Helpers;
using System.Collections;

namespace AssistStudio.Helpers;

/// <summary>
/// Resolves auxiliary providers with automatic fallback to the parent conversation's provider.
/// Validates connectivity via <see cref="IAiProvider.ValidateConnectionAsync"/> with a 2-second
/// timeout as a runtime safety net. On any failure (connection refused, auth error, model not
/// found, timeout), falls back to the parent provider and logs the downgrade.
/// </summary>
/// <remarks>
/// The tab-level preset filter (<c>GetFilteredPresets</c>) removes unreachable providers from
/// dropdowns as a UI convenience. This resolver provides the runtime guarantee — even if the
/// filter hasn't completed (race condition) or the provider goes down mid-session.
/// </remarks>
public sealed class AuxiliaryProviderResolver : IAuxiliaryProviderResolver
{
    /// <summary>Validation timeout for auxiliary providers.</summary>
    static readonly TimeSpan ValidationTimeout = TimeSpan.FromSeconds(5);

    readonly Func<IList> _getAvailableModels;

    /// <summary>
    /// Creates a new resolver.
    /// </summary>
    /// <param name="getAvailableModels">
    /// Delegate returning the current available presets list (from ViewModel).
    /// </param>
    public AuxiliaryProviderResolver(Func<IList> getAvailableModels)
    {
        _getAvailableModels = getAvailableModels;
    }

    /// <inheritdoc/>
    public async Task<IAiProvider> ResolveWithFallbackAsync(
        string? requestedModelName,
        IAiProvider parentProvider,
        string taskType,
        CancellationToken cancellationToken = default)
    {
        // No specific preset requested — use parent directly
        if (string.IsNullOrEmpty(requestedModelName))
            return parentProvider;

        // If the requested preset matches the parent, skip validation
        if (requestedModelName == parentProvider.ProviderName)
            return parentProvider;

        // Find the requested preset in the available list
        ProviderModel? preset = null;
        var presets = _getAvailableModels();
        if (presets is not null)
        {
            foreach (var obj in presets)
            {
                if (obj is ProviderModel p && p.Name == requestedModelName)
                {
                    preset = p;
                    break;
                }
            }
        }

        if (preset is null)
        {
            // Fallback: check full (unfiltered) preset list in case the filtered
            // list excluded the preset due to a reachability race condition.
            foreach (var p in AppSettings.LoadModels())
            {
                if (p.Name == requestedModelName)
                {
                    preset = p;
                    break;
                }
            }
        }

        if (preset is null)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] Preset '{requestedModelName}' not found, falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }

        IAiProvider provider;
        try
        {
            provider = ProviderFactory.Create(preset);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] Failed to create '{requestedModelName}' ({ex.Message}), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }

        // Validate connectivity with timeout.
        // User cancellation propagates; only timeout/connection errors trigger fallback.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ValidationTimeout);

        try
        {
            var info = await provider.ValidateConnectionAsync(timeoutCts.Token);
            if (info.IsValid)
                return provider;

            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] {requestedModelName} validation failed ({info.ErrorMessage}), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // User cancellation — propagate, do NOT fallback
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] {requestedModelName} unreachable (Timeout), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] {requestedModelName} unreachable ({ex.GetType().Name}: {ex.Message}), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }
    }
}
