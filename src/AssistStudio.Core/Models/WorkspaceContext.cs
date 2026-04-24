namespace FieldCure.AssistStudio.Core.Models;

/// <summary>
/// Provides the host application's current workspace state to be automatically
/// injected into every AI request as additional system prompt context.
/// </summary>
/// <remarks>
/// Implement this interface in the host application to let the AI understand
/// what the user is currently viewing (e.g., active dataset, chart type, parameters).
/// The returned text is prepended to the system prompt on each request.
/// </remarks>
public interface IWorkspaceContext
{
    /// <summary>
    /// Brief label shown in the chat UI indicating what context is active
    /// (e.g., "Nyquist Plot — Dataset 3"). Returns <c>null</c> if no context.
    /// </summary>
    string? ActiveLabel { get; }

    /// <summary>
    /// Returns a text description of the current workspace state.
    /// Called before each AI request. Return <c>null</c> or empty to skip injection.
    /// </summary>
    Task<string?> GetContextAsync(CancellationToken ct = default);
}
