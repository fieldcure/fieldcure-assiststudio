namespace FieldCure.AssistStudio.Models;

/// <summary>
/// Contains the result of validating a connection to an AI provider.
/// </summary>
/// <param name="IsValid">Whether the connection was successfully validated.</param>
/// <param name="OrganizationId">The organization identifier returned by the provider, if available.</param>
/// <param name="OrganizationName">The organization display name returned by the provider, if available.</param>
/// <param name="ErrorMessage">An error message describing why validation failed, or <see langword="null"/> on success.</param>
public partial record ConnectionInfo(
    bool IsValid,
    string? OrganizationId,
    string? OrganizationName,
    string? ErrorMessage
);
