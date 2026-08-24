namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetDryDocksResponse(
    IReadOnlyList<DryDockListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
