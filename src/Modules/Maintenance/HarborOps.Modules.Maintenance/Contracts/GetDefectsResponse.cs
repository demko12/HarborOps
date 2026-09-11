namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetDefectsResponse(
    IReadOnlyList<DefectSummary> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
