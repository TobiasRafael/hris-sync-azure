namespace HrSync.Adapters.Hris;

// Vendor shape of GET /hris/employees/{id} (snake_case on the wire).
public sealed record HrisEmployeeDto(
    string? EmployeeId,
    string? FirstName,
    string? LastName,
    string? WorkEmail,
    string? PersonalEmail,
    string? MobilePhone,
    string? JobTitle,
    DateOnly? HireDate);
