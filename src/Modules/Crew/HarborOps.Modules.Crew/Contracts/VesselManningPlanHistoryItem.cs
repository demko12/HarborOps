namespace HarborOps.Modules.Crew.Contracts;

public sealed record VesselManningPlanHistoryItem(
    Guid Id,
    string EventType,
    string? Reason,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    DateOnly? PreviousEffectiveEndDate,
    DateOnly? NewEffectiveEndDate);
