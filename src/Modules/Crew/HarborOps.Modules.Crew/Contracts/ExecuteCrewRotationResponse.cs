namespace HarborOps.Modules.Crew.Contracts;

public sealed record ExecuteCrewRotationResponse(
    Guid AssignmentId,
    DateOnly ActualSignOffDate,
    Guid ReplacementAssignmentId,
    DateOnly ReplacementSignOnDate,
    DateOnly ReplacementPlannedSignOffDate);
