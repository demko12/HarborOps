namespace HarborOps.Api.Composition.Operations;

public sealed record FleetVesselOverviewPage(
    IReadOnlyList<FleetVesselOverviewRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
