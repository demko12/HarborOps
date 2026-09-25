namespace HarborOps.Modules.Crew.Contracts;

public sealed record CreateQualificationPolicyResponse(
    Guid Id,
    Guid CrewPositionId,
    bool Enabled,
    Guid ConcurrencyToken,
    IReadOnlyList<string> RequiredTypes);
