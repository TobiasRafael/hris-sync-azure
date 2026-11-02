namespace HrSync.Functions.Tests;

// A [Fact] that is reported as skipped (not failed) when there is no Azurite to test against:
// neither a Docker engine for Testcontainers nor an existing instance in HRSYNC_TEST_AZURITE.
public sealed class AzuriteFactAttribute : FactAttribute
{
    public AzuriteFactAttribute()
    {
        if (AzuriteFixture.ExistingConnectionString is null && !DockerAvailability.IsAvailable)
            Skip = $"No Azurite: start Docker (Testcontainers) or set {AzuriteFixture.ExistingInstanceVariable} to a running Azurite's connection string.";
    }
}
