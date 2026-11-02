using HrSync.Adapters.Care;
using HrSync.Adapters.Correlation;
using HrSync.Adapters.Hris;
using HrSync.Adapters.Http;
using HrSync.Core.Ports;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using Refit;

namespace HrSync.Adapters.DependencyInjection;

public static class AdaptersServiceCollectionExtensions
{
    // configureResilience runs after the defaults below; tests use it to shorten delays.
    public static IServiceCollection AddHrSyncAdapters(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<HttpStandardResilienceOptions>? configureResilience = null)
    {
        AddVendorOptions(services, configuration, VendorApiOptions.HrisSection);
        AddVendorOptions(services, configuration, VendorApiOptions.CareSection);

        services.AddSingleton<ICorrelationContext, CorrelationContext>();
        services.AddTransient<CorrelationIdHandler>();

        void Resilience(HttpStandardResilienceOptions o)
        {
            ConfigureResilience(o);
            configureResilience?.Invoke(o);
        }

        // Handler order: CorrelationIdHandler (outer) -> resilience (retries, timeouts) -> network.
        services.AddRefitGeneratedClient<IHrisApi>(VendorRefit.Settings)
            .ConfigureHttpClient((sp, http) => http.BaseAddress = BaseAddress(sp, VendorApiOptions.HrisSection))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(Resilience);

        services.AddRefitGeneratedClient<ICareApi>(VendorRefit.Settings)
            .ConfigureHttpClient((sp, http) => http.BaseAddress = BaseAddress(sp, VendorApiOptions.CareSection))
            .AddHttpMessageHandler<CorrelationIdHandler>()
            .AddStandardResilienceHandler(Resilience);

        services.AddTransient<IHrisClient, HrisHttpClient>();
        services.AddTransient<ICareClient, CareHttpClient>();

        return services;
    }

    // In-process retries smooth over short blips; anything longer becomes a TransientIntegrationException
    // and the queue redelivers the message later.
    private static void ConfigureResilience(HttpStandardResilienceOptions o)
    {
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.BackoffType = DelayBackoffType.Exponential;
        o.Retry.UseJitter = true;
        o.Retry.ShouldRetryAfterHeader = true;
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
    }

    private static void AddVendorOptions(IServiceCollection services, IConfiguration configuration, string section) =>
        services.AddOptions<VendorApiOptions>(section)
            .Bind(configuration.GetSection(section))
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                $"{section}:BaseUrl must be an absolute URL ({section}__BaseUrl).")
            .ValidateOnStart();

    private static Uri BaseAddress(IServiceProvider sp, string section) =>
        new(sp.GetRequiredService<IOptionsMonitor<VendorApiOptions>>().Get(section).BaseUrl);
}
