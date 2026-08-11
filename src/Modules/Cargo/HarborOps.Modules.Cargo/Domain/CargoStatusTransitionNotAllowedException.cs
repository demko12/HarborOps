namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoStatusTransitionNotAllowedException(
    Guid cargoBookingId,
    CargoStatus currentStatus,
    CargoStatus requestedStatus)
    : Exception(
        $"Cargo booking '{cargoBookingId}' cannot move from status '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal CargoStatus CurrentStatus { get; } = currentStatus;

    internal CargoStatus RequestedStatus { get; } = requestedStatus;
}
