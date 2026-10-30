namespace HrSync.Adapters.Care;

// Vendor shape of the care API worker resource (snake_case on the wire).
public sealed record CareWorkerDto(
    string? ExternalId,
    string? GivenName,
    string? FamilyName,
    string? Email,
    string? Phone,
    string? Role,
    DateOnly? StartDate);
