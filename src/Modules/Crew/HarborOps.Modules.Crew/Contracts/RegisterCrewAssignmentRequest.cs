namespace HarborOps.Modules.Crew.Contracts;

public sealed record RegisterCrewAssignmentRequest
{
    public Guid? CrewMemberId { get; init; }
    public Guid? VesselId { get; init; }
    public Guid? RankId { get; init; }
    public Guid? SignOnPortId { get; init; }
    public DateOnly? SignOnDate { get; init; }
    public DateOnly? PlannedSignOffDate { get; init; }
}
