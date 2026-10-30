using System.Net;
using HrSync.Adapters.Http;
using HrSync.Core.Models;
using HrSync.Core.Ports;

namespace HrSync.Adapters.Hris;

// Typed HttpClient; BaseAddress comes from HrisApi:BaseUrl.
public sealed class HrisHttpClient(HttpClient http) : IHrisClient
{
    private const string SystemName = "HRIS";

    public async Task<Employee?> GetEmployeeAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"hris/employees/{Uri.EscapeDataString(id)}");
        using var response = await VendorHttp.SendAsync(http, request, SystemName, allowNotFound: true, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        var dto = await VendorHttp.ReadJsonAsync<HrisEmployeeDto>(response, SystemName, ct);
        return new Employee
        {
            Id = string.IsNullOrWhiteSpace(dto.EmployeeId) ? id : dto.EmployeeId,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            WorkEmail = dto.WorkEmail,
            PersonalEmail = dto.PersonalEmail,
            MobilePhone = dto.MobilePhone,
            JobTitle = dto.JobTitle,
            HireDate = dto.HireDate,
        };
    }
}
