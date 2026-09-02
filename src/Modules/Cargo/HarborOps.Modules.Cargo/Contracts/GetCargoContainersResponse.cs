namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetCargoContainersResponse(
    IReadOnlyList<CargoContainerListItem> Items);
