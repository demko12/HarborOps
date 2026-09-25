namespace HarborOps.Modules.Crew.Contracts;

public sealed record CreateQualificationPolicyRequest
{
    public Guid? CrewPositionId { get; init; }
    public bool? Enabled { get; init; }
    public IReadOnlyList<QualificationRequirementInput>? Requirements { get; init; }
}
