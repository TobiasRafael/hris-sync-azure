namespace HrSync.Core.Errors;

// Retrying won't help (bad data, missing record, 4xx). The message goes to the poison queue.
public sealed class PermanentIntegrationException(string reason, Exception? innerException = null)
    : Exception(reason, innerException)
{
    public string Reason { get; } = reason;
}
