namespace HarborOps.Modules.Voyages.Contracts;

public sealed record SearchRoutesResponse(
    IReadOnlyList<RouteListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
