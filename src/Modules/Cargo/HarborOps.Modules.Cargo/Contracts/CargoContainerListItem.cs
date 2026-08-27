namespace HarborOps.Modules.Cargo.Contracts;

public sealed record CargoContainerListItem(
    Guid ContainerId,
    string ContainerNumber,
    string ContainerType,
    string Status,
    Guid CargoBookingId,
    Guid? VesselId);
