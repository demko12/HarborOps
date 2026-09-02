namespace HarborOps.Modules.Crew.Contracts;

public sealed record ChangeCrewMemberEmploymentStatusResponse(
    Guid Id,
    Guid CrewMemberId,
    string PreviousStatus,
    string NewStatus,
    Guid ChangedByUserId,
    string? Notes,
    DateTime ChangedAtUtc);
