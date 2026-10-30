namespace HrSync.MockApis.Faults;

// What the middleware should do for one request. Failure is null when the request passes through.
public sealed record FaultDecision(int DelayMs, FaultRule? Failure);
