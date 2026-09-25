namespace HarborOps.Modules.Crew.Contracts;

public sealed record QualificationPolicyListItem(
    Guid Id,
    Guid CrewPositionId,
    string RankName,
    bool Enabled,
    int RequiredTypeCount,
    Guid ConcurrencyToken);
