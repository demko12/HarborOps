namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetVesselManningPlansResponse(
    IReadOnlyList<VesselManningPlanListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
