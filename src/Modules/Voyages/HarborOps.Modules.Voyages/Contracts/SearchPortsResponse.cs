namespace HarborOps.Modules.Voyages.Contracts;

public sealed record SearchPortsResponse(
    IReadOnlyList<PortListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
