namespace HarborOps.Modules.Voyages.Contracts;

public sealed record GetVoyagesResponse(
    IReadOnlyList<VoyageListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
