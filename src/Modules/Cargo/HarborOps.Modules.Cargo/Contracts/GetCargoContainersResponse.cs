namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetCargoContainersResponse(
    IReadOnlyList<CargoContainerListItem> Items,
    int TotalCount = 0,
    int PageNumber = 1,
    int PageSize = 15,
    int LoadedCount = 0,
    int MatchingCount = 0);
