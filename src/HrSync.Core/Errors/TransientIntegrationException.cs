namespace HrSync.Core.Errors;

// Might succeed later (5xx, 408, 429, timeouts, event claimed elsewhere). The queue retries it.
public sealed class TransientIntegrationException(string message, Exception? innerException = null)
    : Exception(message, innerException);
