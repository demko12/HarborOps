namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewAvailabilityConflictsRequest
{
    public Guid? CrewMemberId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}
