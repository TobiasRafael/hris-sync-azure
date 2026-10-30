namespace HrSync.MockApis.Faults;

public sealed record FaultError(string Error, string Target, int Status);
