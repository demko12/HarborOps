namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewMemberAvailabilityItem(
    Guid Id,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    string? Notes,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    string? DecisionReason,
    IReadOnlyList<CrewAvailabilityTransitionItem> History);
