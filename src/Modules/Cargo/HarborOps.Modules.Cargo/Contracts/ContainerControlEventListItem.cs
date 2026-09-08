namespace HarborOps.Modules.Cargo.Contracts;

public sealed record ContainerControlEventListItem(
    Guid ContainerControlEventId,
    string Action,
    string FromState,
    string ToState,
    string? SealNumber,
    string? InspectionResult,
    decimal? TemperatureCelsius,
    string? Notes,
    Guid ActorUserId,
    DateTime OccurredUtc);
