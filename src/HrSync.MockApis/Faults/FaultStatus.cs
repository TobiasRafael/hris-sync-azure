namespace HrSync.MockApis.Faults;

public sealed record FaultStatus(FaultRule Rule, int Remaining);
