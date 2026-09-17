namespace HarborOps.Modules.Cargo.Contracts;

public sealed record BillOfLadingEventListItem(
    Guid BillOfLadingEventId,
    string Action,
    string FromStatus,
    string ToStatus,
    string? Notes,
    Guid ActorUserId,
    DateTime OccurredUtc);
