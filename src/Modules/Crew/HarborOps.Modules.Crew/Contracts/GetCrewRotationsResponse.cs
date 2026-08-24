namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewRotationsResponse(
    IReadOnlyList<CrewRotationListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
