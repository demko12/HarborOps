namespace HarborOps.Modules.Crew.Domain;

internal sealed class CrewQualificationRequirement
{
    internal const int RequiredTypeMaxLength = 100;

    private CrewQualificationRequirement()
    {
    }

    private CrewQualificationRequirement(Guid id, Guid policyId, string requiredType)
    {
        Id = id;
        PolicyId = policyId;
        RequiredType = requiredType;
    }

    public Guid Id { get; private set; }
    public Guid PolicyId { get; private set; }
    public string RequiredType { get; private set; } = null!;

    internal static CrewQualificationRequirement Create(Guid policyId, string requiredType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredType);

        return new CrewQualificationRequirement(Guid.NewGuid(), policyId, requiredType);
    }
}
