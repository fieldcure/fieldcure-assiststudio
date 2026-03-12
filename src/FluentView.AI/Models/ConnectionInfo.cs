namespace FluentView.AI.Models;

public partial record ConnectionInfo(
    bool IsValid,
    string? OrganizationId,
    string? OrganizationName,
    string? ErrorMessage
);
