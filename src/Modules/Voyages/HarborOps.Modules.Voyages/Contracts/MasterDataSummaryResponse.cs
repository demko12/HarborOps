namespace HarborOps.Modules.Voyages.Contracts;

public sealed record MasterDataSummaryResponse(
    int PortCount,
    int BerthCount,
    int RouteCount,
    decimal AverageRouteDistance);
