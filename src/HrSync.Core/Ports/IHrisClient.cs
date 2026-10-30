using HrSync.Core.Models;

namespace HrSync.Core.Ports;

public interface IHrisClient
{
    // Null when the HRIS has no such employee.
    Task<Employee?> GetEmployeeAsync(string id, CancellationToken ct);
}
