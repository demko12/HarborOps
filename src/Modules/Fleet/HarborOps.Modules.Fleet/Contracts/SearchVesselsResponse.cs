namespace HarborOps.Modules.Fleet.Contracts;

public sealed record SearchVesselsResponse(
    IReadOnlyList<VesselListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
