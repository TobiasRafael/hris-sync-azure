namespace HrSync.MockApis.Care;

public sealed record CareWorkerDto(
    string? ExternalId,
    string? GivenName,
    string? FamilyName,
    string? Email,
    string? Phone,
    string? Role,
    DateOnly? StartDate);
