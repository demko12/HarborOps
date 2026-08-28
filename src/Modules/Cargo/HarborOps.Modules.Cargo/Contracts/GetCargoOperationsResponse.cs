namespace HarborOps.Modules.Cargo.Contracts;

public sealed record GetCargoOperationsResponse(
    IReadOnlyList<CargoOperationListItem> Items);
