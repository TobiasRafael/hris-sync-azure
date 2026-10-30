namespace HrSync.MockApis.Hris;

public sealed record HrisEmployee(
    string EmployeeId,
    string? FirstName,
    string? LastName,
    string? WorkEmail,
    string? PersonalEmail,
    string? MobilePhone,
    string? JobTitle,
    DateOnly? HireDate);
