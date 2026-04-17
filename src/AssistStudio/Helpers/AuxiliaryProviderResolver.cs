using FieldCure.Ai.Providers;
using FieldCure.Ai.Providers.Models;
using FieldCure.AssistStudio.Controls;
using FieldCure.AssistStudio.Helpers;
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

    readonly Func<IList> _getAvailablePresets;

    /// <summary>
    /// Creates a new resolver.
    /// </summary>
    /// <param name="getAvailablePresets">
    /// Delegate returning the current available presets list (from ViewModel).
    /// </param>
    public AuxiliaryProviderResolver(Func<IList> getAvailablePresets)
    {
        _getAvailablePresets = getAvailablePresets;
    }

    /// <inheritdoc/>
    public async Task<IAiProvider> ResolveWithFallbackAsync(
        string? requestedPresetName,
        IAiProvider parentProvider,
        string taskType,
        CancellationToken cancellationToken = default)
    {
        // No specific preset requested — use parent directly
        if (string.IsNullOrEmpty(requestedPresetName))
            return parentProvider;

        // If the requested preset matches the parent, skip validation
        if (requestedPresetName == parentProvider.ProviderName)
            return parentProvider;

        // Find the requested preset in the available list
        ProviderPreset? preset = null;
        var presets = _getAvailablePresets();
        if (presets is not null)
        {
            foreach (var obj in presets)
            {
                if (obj is ProviderPreset p && p.Name == requestedPresetName)
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
            foreach (var p in AppSettings.LoadPresets())
            {
                if (p.Name == requestedPresetName)
                {
                    preset = p;
                    break;
                }
            }
        }

        if (preset is null)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] Preset '{requestedPresetName}' not found, falling back to {parentProvider.ProviderName}");
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
                $"[AuxProvider][{taskType}] Failed to create '{requestedPresetName}' ({ex.Message}), falling back to {parentProvider.ProviderName}");
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
                $"[AuxProvider][{taskType}] {requestedPresetName} validation failed ({info.ErrorMessage}), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // User cancellation — propagate, do NOT fallback
        }
        catch (OperationCanceledException)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] {requestedPresetName} unreachable (Timeout), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogWarning(
                $"[AuxProvider][{taskType}] {requestedPresetName} unreachable ({ex.GetType().Name}: {ex.Message}), falling back to {parentProvider.ProviderName}");
            return parentProvider;
        }
    }
}
