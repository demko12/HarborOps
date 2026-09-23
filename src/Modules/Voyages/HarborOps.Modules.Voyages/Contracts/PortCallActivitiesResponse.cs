namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortCallActivitiesResponse(
    Guid VoyageId,
    Guid StopId,
    IReadOnlyList<PortCallActivityItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
