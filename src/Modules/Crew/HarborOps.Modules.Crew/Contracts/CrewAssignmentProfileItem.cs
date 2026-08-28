namespace HarborOps.Modules.Crew.Contracts;

public sealed record CrewAssignmentProfileItem(
    Guid AssignmentId,
    Guid VesselId,
    Guid RankId,
    string RankName,
    Guid? SignOnPortId,
    Guid? SignOffPortId,
    DateOnly SignOnDate,
    DateOnly PlannedSignOffDate,
    DateOnly? ActualSignOffDate,
    string Status,
    DateTime? CancelledAtUtc = null,
    Guid? CancelledByUserId = null,
    string? CancellationNotes = null);
