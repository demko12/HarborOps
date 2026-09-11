namespace HarborOps.Modules.Crew.Contracts;

public sealed record GetCrewAvailabilityResponse(
    IReadOnlyList<CrewAvailabilityListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
