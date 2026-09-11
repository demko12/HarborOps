namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallClearanceStatusResponse(
    Guid ClearanceId,
    Guid StopId,
    string Status,
    Guid ActorUserId,
    DateTime OccurredAtUtc);
