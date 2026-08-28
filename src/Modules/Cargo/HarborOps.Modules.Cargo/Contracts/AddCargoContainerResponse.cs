namespace HarborOps.Modules.Cargo.Contracts;

public sealed record AddCargoContainerResponse(
    Guid Id,
    string ContainerNumber,
    string Status);
