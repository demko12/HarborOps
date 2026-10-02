namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancyContainerMismatchException(
    Guid containerId,
    Guid cargoBookingId)
    : Exception($"Container '{containerId}' does not belong to cargo booking '{cargoBookingId}'.")
{
    internal Guid ContainerId { get; } = containerId;

    internal Guid CargoBookingId { get; } = cargoBookingId;
}
