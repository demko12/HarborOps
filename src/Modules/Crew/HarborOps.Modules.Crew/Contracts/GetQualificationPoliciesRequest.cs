namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetQualificationPoliciesRequest
{
    public Guid? CrewPositionId { get; init; }
    public int? PageNumber { get; init; }
    public int? PageSize { get; init; }
}
