namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetQualificationPolicyDetailResponse(
    Guid Id,
    Guid CrewPositionId,
    string RankName,
    bool Enabled,
    Guid ConcurrencyToken,
    IReadOnlyList<string> RequiredTypes,
    IReadOnlyList<QualificationPolicyHistoryItem> History);
