namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallClearanceEventModel(
    Guid EventId,
    Guid? ItemId,
    int Sequence,
    string EventType,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc);
