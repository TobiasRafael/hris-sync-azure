namespace HrSync.Core.Models
{
    public sealed record CareWorker
    {
        public string? ExternalId { get; init; }
        public string? GivenName { get; init; }
        public string? FamilyName { get; init; }
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public string? Role { get; init; }
        public DateOnly? StartDate { get; init; }
    }
}
