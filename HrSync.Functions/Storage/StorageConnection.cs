namespace HrSync.Functions.Storage;

// Connection names shared by the triggers/bindings and the Aspire storage clients. They must equal the
// AppHost's storage resource names. The AppHost injects "queues"/"tables" (read by the triggers) and
// Aspire__Azure__Storage__Queues__queues__ConnectionString / Aspire__Azure__Data__Tables__tables__ConnectionString
// (read by the clients). local.settings.json sets the same keys for standalone `func start`; don't use
// ConnectionStrings__queues there, because it takes precedence and would hide the AppHost's emulator.
public static class StorageConnection
{
    public const string Queues = "queues";
    public const string Tables = "tables";
}
