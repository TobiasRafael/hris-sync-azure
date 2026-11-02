using Testcontainers.Azurite;

namespace HrSync.Functions.Tests;

// One Azurite per test class: a Testcontainers container by default, or an already running instance when
// HRSYNC_TEST_AZURITE is set (e.g. "UseDevelopmentStorage=true" on a machine without Docker).
// Nothing is started when neither is available; the [AzuriteFact] tests skip themselves.
public sealed class AzuriteFixture : IAsyncLifetime
{
    public const string ExistingInstanceVariable = "HRSYNC_TEST_AZURITE";

    private const string Image = "mcr.microsoft.com/azure-storage/azurite:3.37.0";

    private AzuriteContainer? _container;

    public static string? ExistingConnectionString =>
        Environment.GetEnvironmentVariable(ExistingInstanceVariable) is { Length: > 0 } value ? value : null;

    public string ConnectionString =>
        ExistingConnectionString
        ?? _container?.GetConnectionString()
        ?? throw new InvalidOperationException("No Azurite available.");

    public async Task InitializeAsync()
    {
        if (ExistingConnectionString is not null || !DockerAvailability.IsAvailable)
            return;

        _container = new AzuriteBuilder(Image).WithInMemoryPersistence().Build();
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
            await _container.DisposeAsync();
    }
}
