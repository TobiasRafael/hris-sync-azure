using Azure.Data.Tables;
using Azure.Storage.Queues;
using HrSync.Adapters.DependencyInjection;
using HrSync.Core.Mapping;
using HrSync.Core.Mappings;
using HrSync.Core.Ports;
using HrSync.Core.Processing;
using HrSync.Functions.Idempotency;
using HrSync.Functions.NewHires;
using HrSync.Functions.Storage;
using HrSync.Functions.Webhooks;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// OpenTelemetry (OTLP to the Aspire dashboard when the AppHost sets OTEL_EXPORTER_OTLP_ENDPOINT),
// health checks, service discovery, HttpClient resilience.
builder.AddServiceDefaults();
// Picks up the host's trace context for each invocation and tags spans with function metadata.
// The "HrSync" ActivitySource (HrSyncTelemetry.SourceName) is added by ServiceDefaults.
builder.Services.AddOpenTelemetry().UseFunctionsWorkerDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<WebhookOptions>()
    .BindConfiguration(WebhookOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Webhook:Secret is required (Webhook__Secret).")
    .ValidateOnStart();

// Field mapping is config-only (appsettings.json, or FieldMapping__Fields__<CareWorker prop> = <Employee prop>);
// invalid config stops startup.
builder.Services.AddOptions<FieldMappingOptions>()
    .BindConfiguration("FieldMapping")
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FieldMappingOptions>, FieldMappingValidator>();
builder.Services.AddSingleton(sp => new FieldMapper(sp.GetRequiredService<IOptions<FieldMappingOptions>>().Value));

builder.Services.AddHrSyncAdapters(builder.Configuration);
// Transient: it holds typed HttpClients, which shouldn't be captured by a singleton.
builder.Services.AddTransient<NewHireProcessor>();

// Same connections as the triggers ("queues", "tables"): injected by the AppHost, or from local.settings.json.
// Base64 matches what the Functions queue extension writes and expects (poison envelopes).
builder.AddAzureQueueServiceClient(StorageConnection.Queues,
    configureClientBuilder: b => b.ConfigureOptions(o => o.MessageEncoding = QueueMessageEncoding.Base64));
builder.AddAzureTableServiceClient(StorageConnection.Tables);

builder.Services.AddSingleton<IIdempotencyStore>(sp => new TableIdempotencyStore(
    sp.GetRequiredService<TableServiceClient>().GetTableClient(TableIdempotencyStore.TableName)));
builder.Services.AddSingleton<IPoisonQueue>(sp => new StoragePoisonQueue(
    sp.GetRequiredService<QueueServiceClient>().GetQueueClient(StoragePoisonQueue.QueueName)));

builder.Build().Run();
