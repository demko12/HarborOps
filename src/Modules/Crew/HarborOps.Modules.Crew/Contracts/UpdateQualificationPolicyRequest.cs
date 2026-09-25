namespace HarborOps.Modules.Crew.Contracts;

public sealed record UpdateQualificationPolicyRequest
{
    public bool? Enabled { get; init; }
    public IReadOnlyList<QualificationRequirementInput>? Requirements { get; init; }
}
