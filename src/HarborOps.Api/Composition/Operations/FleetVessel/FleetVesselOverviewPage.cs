namespace HarborOps.Api.Composition.Operations.FleetVessel;

public sealed record FleetVesselOverviewPage(
    IReadOnlyList<FleetVesselOverviewRow> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
