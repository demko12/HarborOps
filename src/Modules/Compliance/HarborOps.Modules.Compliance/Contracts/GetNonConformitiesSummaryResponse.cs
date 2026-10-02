namespace HarborOps.Modules.Compliance.Contracts;

public sealed record GetNonConformitiesSummaryResponse(
    int UnresolvedCriticalCount,
    int UnresolvedMajorCount,
    int UnresolvedMinorCount,
    int OverdueCount);
