namespace HarborOps.Modules.Crew.Contracts;

public sealed record CoverageDayItem(
    DateOnly Date,
    Guid RankId,
    string RankName,
    int? RequiredHeadcount,
    int ProjectedAssignedHeadcount,
    int EligibleHeadcount,
    int? Shortage,
    IReadOnlyList<CoverageIneligibleMember> IneligibleMembers,
    IReadOnlyList<Guid> AssignmentIds,
    int? QualifiedHeadcount,
    IReadOnlyList<CoverageUnqualifiedMember> UnqualifiedMembers);
