namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewAvailabilityDecisionResponse(
    Guid Id,
    Guid CrewMemberId,
    string PreviousStatus,
    string NewStatus,
    Guid ActorUserId,
    string? Reason,
    DateTime OccurredAtUtc);
