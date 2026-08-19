namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoTrackingEventListItem(
    Guid CargoTrackingEventId,
    string Status,
    DateTime OccurredUtc,
    string Location,
    Guid ResponsibleUserId);
