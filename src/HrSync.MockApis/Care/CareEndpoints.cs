using Microsoft.AspNetCore.Http.HttpResults;

namespace HrSync.MockApis.Care;

public sealed record CareWorkerDto(
    string? ExternalId,
    string? GivenName,
    string? FamilyName,
    string? Email,
    string? Phone,
    string? Role,
    DateOnly? StartDate);

public static class CareEndpoints
{
    public static IEndpointRouteBuilder MapCare(this IEndpointRouteBuilder app)
    {
        var care = app.MapGroup("/care").WithTags("Mock Care API");

        care.MapGet("/workers", (MockState state) =>
            TypedResults.Ok(state.CareWorkers.Values.OrderBy(w => w.ExternalId, StringComparer.Ordinal).ToList()));

        care.MapGet("/workers/{externalId}", Results<Ok<CareWorkerDto>, NotFound> (string externalId, MockState state) =>
            state.CareWorkers.TryGetValue(externalId, out var worker)
                ? TypedResults.Ok(worker)
                : TypedResults.NotFound());

        // Full-replace upsert: the stored record becomes exactly the request body.
        care.MapPut("/workers/{externalId}", Results<Created<CareWorkerDto>, Ok<CareWorkerDto>, ValidationProblem> (
            string externalId, CareWorkerDto body, MockState state) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(body.GivenName))
                errors["given_name"] = ["given_name is required."];
            if (string.IsNullOrWhiteSpace(body.FamilyName))
                errors["family_name"] = ["family_name is required."];
            if (errors.Count > 0)
                return TypedResults.ValidationProblem(errors);

            var worker = body with { ExternalId = externalId };
            if (state.CareWorkers.TryAdd(externalId, worker))
                return TypedResults.Created($"/care/workers/{externalId}", worker);

            state.CareWorkers[externalId] = worker;
            return TypedResults.Ok(worker);
        });

        return app;
    }
}
