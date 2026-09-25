namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetVesselReadinessOverviewResponse(
    DateOnly Today,
    DateOnly RequiredThrough,
    IReadOnlyList<VesselReadinessOverviewItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
