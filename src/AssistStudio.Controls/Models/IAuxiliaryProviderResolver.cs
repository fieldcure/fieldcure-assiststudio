using FieldCure.Ai.Providers;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// Resolves an auxiliary provider (for title, summary, sub-agent, etc.)
/// with automatic fallback to the parent conversation's provider on failure.
/// </summary>
/// <remarks>
/// All background tasks share the same fallback policy: attempt the requested
/// model, validate connectivity, and silently fall back to the parent
/// conversation's (already-reachable) provider on any failure — connection
/// refused, auth error, model not found, or timeout.
/// </remarks>
public interface IAuxiliaryProviderResolver
{
    /// <summary>
    /// Attempts to resolve the specified model; on reachability or auth failure,
    /// falls back to <paramref name="parentProvider"/> and logs the downgrade.
    /// </summary>
    /// <param name="requestedModelName">
    /// ProviderModel name to resolve. <see langword="null"/> returns <paramref name="parentProvider"/> directly.
    /// </param>
    /// <param name="parentProvider">
    /// The parent conversation's provider, already verified as reachable.
    /// </param>
    /// <param name="taskType">
    /// Log identifier for the calling task (e.g., "Title", "Summary", "SubAgent").
    /// </param>
    /// <param name="cancellationToken">
    /// External cancellation (e.g., user cancel). User cancellation is <b>not</b>
    /// treated as a fallback condition and propagates as <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>A usable <see cref="IAiProvider"/> — either the requested one or the parent fallback.</returns>
    Task<IAiProvider> ResolveWithFallbackAsync(
        string? requestedModelName,
        IAiProvider parentProvider,
        string taskType,
        CancellationToken cancellationToken = default);
}
