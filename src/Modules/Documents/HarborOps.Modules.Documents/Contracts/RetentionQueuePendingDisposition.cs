namespace HarborOps.Modules.Documents.Contracts;

public sealed record RetentionQueuePendingDisposition(
    Guid Id,
    Guid RequestedByUserId,
    DateTime RequestedAtUtc);
