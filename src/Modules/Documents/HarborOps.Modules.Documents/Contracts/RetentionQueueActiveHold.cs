namespace HarborOps.Modules.Documents.Contracts;

public sealed record RetentionQueueActiveHold(
    Guid Id,
    string Reason,
    DateTime PlacedAtUtc);
