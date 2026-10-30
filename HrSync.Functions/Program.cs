using Azure.Monitor.OpenTelemetry.Exporter;
using HrSync.Functions.Webhooks;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<WebhookOptions>()
    .BindConfiguration(WebhookOptions.SectionName)
    .Validate(o => !string.IsNullOrWhiteSpace(o.Secret), "Webhook:Secret is required (Webhook__Secret).")
    .ValidateOnStart();

if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")))
{
    builder.Services.AddOpenTelemetry()
        .UseFunctionsWorkerDefaults()
        .UseAzureMonitorExporter();
}

builder.Build().Run();
