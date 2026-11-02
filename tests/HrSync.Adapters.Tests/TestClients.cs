using HrSync.Adapters.Care;
using HrSync.Adapters.Hris;
using HrSync.Adapters.Http;
using Refit;

namespace HrSync.Adapters.Tests;

// Real clients over real Refit proxies, but without the resilience handler: one attempt per call.
internal static class TestClients
{
    public static HrisHttpClient Hris(HttpMessageHandler handler, string baseUrl = "http://hris.test/") =>
        new(RestService.ForGenerated<IHrisApi>(new HttpClient(handler) { BaseAddress = new Uri(baseUrl) }, VendorRefit.Settings));

    public static CareHttpClient Care(HttpMessageHandler handler, string baseUrl = "http://care.test/") =>
        new(RestService.ForGenerated<ICareApi>(new HttpClient(handler) { BaseAddress = new Uri(baseUrl) }, VendorRefit.Settings));
}
