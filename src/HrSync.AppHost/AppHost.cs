var builder = DistributedApplication.CreateBuilder(args);

// Shared HMAC key for the mock HRIS (signs) and the receiver (verifies).
// Dev value: dotnet user-secrets set "Parameters:webhook-secret" "<value>" --project src/HrSync.AppHost
var webhookSecret = builder.AddParameter("webhook-secret", secret: true);

var storage = builder.AddAzureStorage("storage").RunAsEmulator();
var queues = storage.AddQueues("queues");
var tables = storage.AddTables("tables");

// Port 5080 is the proxied endpoint from the "http" launch profile, so requests.http keeps working.
var mockApis = builder.AddProject<Projects.HrSync_MockApis>("mockapis", launchProfileName: "http")
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Webhook__Secret", webhookSecret);

var functions = builder.AddAzureFunctionsProject<Projects.HrSync_Functions>("functions")
    .WithHostStorage(storage)
    .WithReference(queues).WaitFor(queues)
    .WithReference(tables).WaitFor(tables)
    // Service discovery: ServiceDefaults' resolver also runs on the Refit vendor clients, so "http://mockapis"
    // resolves through the services__mockapis__http__0 value that WithReference injects.
    .WithReference(mockApis).WaitFor(mockApis)
    .WithEnvironment("HrisApi__BaseUrl", "http://mockapis")
    .WithEnvironment("CareApi__BaseUrl", "http://mockapis")
    .WithEnvironment("Webhook__Secret", webhookSecret)
    .WithEndpoint("http", e => e.Port = 7071);

// The mock needs the receiver's URL and the receiver needs the mock: a plain environment value
// (no WithReference/WaitFor this way round) avoids a reference cycle. Webhooks sent before the
// Functions host is up are reported as delivery errors by the mock, which is the intended behaviour.
mockApis.WithEnvironment(ctx =>
    ctx.EnvironmentVariables["Webhook__Url"] =
        ReferenceExpression.Create($"{functions.GetEndpoint("http")}/api/webhooks/hris"));

builder.Build().Run();
