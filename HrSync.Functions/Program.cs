using Azure.Data.Tables;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Queues;
using HrSync.Adapters.DependencyInjection;
using HrSync.Core.Mapping;
using HrSync.Core.Mappings;
using HrSync.Core.Ports;
using HrSync.Core.Processing;
using HrSync.Functions.Idempotency;
using HrSync.Functions.NewHires;
using HrSync.Functions.Webhooks;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<WebhookOptions>()
    .BindConfiguration(WebhookOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Webhook:Secret is required (Webhook__Secret).")
    .ValidateOnStart();

// Field mapping is config-only (FieldMapping__Fields__<CareWorker prop> = <Employee prop>); invalid config stops startup.
builder.Services.AddOptions<FieldMappingOptions>()
    .BindConfiguration("FieldMapping")
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<FieldMappingOptions>, FieldMappingValidator>();
builder.Services.AddSingleton(sp => new FieldMapper(sp.GetRequiredService<IOptions<FieldMappingOptions>>().Value));

builder.Services.AddHrSyncAdapters(builder.Configuration);
// Transient: it holds typed HttpClients, which shouldn't be captured by a singleton.
builder.Services.AddTransient<NewHireProcessor>();

// Same storage account as the queue triggers.
var storage = builder.Configuration["AzureWebJobsStorage"];
if (string.IsNullOrWhiteSpace(storage))
    throw new InvalidOperationException("AzureWebJobsStorage is required (idempotency table and poison queue).");

builder.Services.AddSingleton<IIdempotencyStore>(_ =>
    new TableIdempotencyStore(new TableClient(storage, TableIdempotencyStore.TableName)));
builder.Services.AddSingleton<IPoisonQueue>(_ =>
    new StoragePoisonQueue(new QueueClient(storage, StoragePoisonQueue.QueueName,
        new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 })));

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
