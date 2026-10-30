using HrSync.Core.Models;
using HrSync.Core.Rules;

namespace HrSync.Core.Tests;

public class CareWorkerMergeTests
{
    private static readonly CareWorker Existing = new()
    {
        ExternalId = "E-1001",
        GivenName = "Alice",
        FamilyName = "Smith",
        Email = "alice@contoso.test",
        Phone = "+1-555-0100",
        Role = "Caregiver",
        StartDate = new DateOnly(2026, 11, 2),
    };

    [Fact]
    public void Non_empty_incoming_value_wins()
    {
        var result = CareWorkerMerge.Apply(Existing, Existing with { Role = "Senior Caregiver" });
        Assert.Equal("Senior Caregiver", result.Role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_incoming_value_never_overwrites(string? phone)
    {
        var result = CareWorkerMerge.Apply(Existing, Existing with { Phone = phone });
        Assert.Equal("+1-555-0100", result.Phone);
    }

    [Fact]
    public void Null_date_never_overwrites()
    {
        var result = CareWorkerMerge.Apply(Existing, Existing with { StartDate = null });
        Assert.Equal(new DateOnly(2026, 11, 2), result.StartDate);
    }

    [Fact]
    public void Sparse_update_keeps_everything_else()
    {
        var incoming = new CareWorker { ExternalId = "E-1001", Email = "alice.smith@contoso.test" };
        var result = CareWorkerMerge.Apply(Existing, incoming);

        Assert.Equal(Existing with { Email = "alice.smith@contoso.test" }, result); 
    }
}