namespace HarborOps.Modules.Crew.Contracts;

public sealed record UpdateQualificationPolicyResponse(
    Guid Id,
    bool Enabled,
    Guid ConcurrencyToken,
    IReadOnlyList<string> RequiredTypes);
