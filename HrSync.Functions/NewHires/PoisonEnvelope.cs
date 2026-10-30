using HrSync.Core.Models;

namespace HrSync.Functions.NewHires;

// What ProcessNewHire writes to "new-hires-poison" for a permanent failure. (Messages the runtime moves
// there after maxDequeueCount are the raw NewHireMessage instead.)
public sealed record PoisonEnvelope(NewHireMessage OriginalMessage, string Reason, DateTimeOffset FailedAt);
