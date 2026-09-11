namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewAvailabilityListItem(
    Guid Id,
    Guid CrewMemberId,
    string CrewMemberName,
    string Type,
    string Status,
    DateOnly StartDate,
    DateOnly EndDate,
    string Reason,
    string? Notes,
    Guid RequestedByUserId,
    DateTime RequestedAtUtc,
    Guid? ReviewedByUserId,
    DateTime? ReviewedAtUtc,
    string? DecisionReason);
