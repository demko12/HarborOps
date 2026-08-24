namespace HarborOps.Modules.Crew.Contracts;

public sealed record PlanCrewRotationRequest
{
    public Guid? RelievingCrewMemberId { get; init; }

    public string? RotationPattern { get; init; }

    public DateOnly? PlannedReliefDate { get; init; }
}
