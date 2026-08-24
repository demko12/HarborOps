namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewRotationListItem(
    Guid RotationId,
    Guid AssignmentId,
    Guid VesselId,
    Guid CrewMemberId,
    string CrewMemberName,
    Guid RankId,
    string RankName,
    Guid? RelievingCrewMemberId,
    string? RelievingCrewMemberName,
    string RotationPattern,
    DateOnly PlannedReliefDate,
    bool IsOverdue);
