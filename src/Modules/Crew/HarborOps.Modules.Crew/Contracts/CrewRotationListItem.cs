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
    bool IsOverdue,
    string Status = "Planned",
    DateTime? ExecutedAtUtc = null,
    Guid? ExecutedByUserId = null,
    Guid? ReplacementAssignmentId = null,
    DateTime? CancelledAtUtc = null,
    Guid? CancelledByUserId = null,
    string? CancellationNotes = null);
