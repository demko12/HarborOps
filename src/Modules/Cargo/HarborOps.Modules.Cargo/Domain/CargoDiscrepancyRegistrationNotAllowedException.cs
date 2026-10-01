namespace HarborOps.Modules.Cargo.Domain;

internal sealed class CargoDiscrepancyRegistrationNotAllowedException(
    Guid cargoBookingId,
    CargoStatus bookingStatus)
    : Exception(
        $"Discrepancies cannot be registered for cargo booking '{cargoBookingId}' while it is '{bookingStatus}'. Registration is open from Loaded until Unloaded.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal CargoStatus BookingStatus { get; } = bookingStatus;
}
