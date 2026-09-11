namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewAvailabilityTransitionItem(
    Guid Id,
    string EventType,
    string? Reason,
    DateTime OccurredAtUtc,
    string PreviousStatus,
    string NewStatus);
