namespace HarborOps.Modules.Voyages.Contracts;

public sealed record SearchBerthsResponse(
    IReadOnlyList<BerthListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
