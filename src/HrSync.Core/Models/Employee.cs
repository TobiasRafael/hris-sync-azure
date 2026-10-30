namespace HrSync.Core.Models
{
    public sealed record Employee
    {
        public required string Id { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public string? WorkEmail { get; init; }
        public string? PersonalEmail { get; init; }
        public string? MobilePhone { get; init; }
        public string? JobTitle { get; init; }
        public DateOnly? HireDate { get; init; }
    }
}
