namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewQualificationEligibilityRequest
{
    public Guid? CrewMemberId { get; init; }
    public Guid? RankId { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
}
