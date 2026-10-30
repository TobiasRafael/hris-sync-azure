namespace HrSync.MockApis.Faults;

public sealed record FaultRule
{
    public int FailNext { get; init; }
    public int StatusCode { get; init; } = 503;
    public int? RetryAfterSeconds { get; init; }
    public int DelayMs { get; init; }
}
