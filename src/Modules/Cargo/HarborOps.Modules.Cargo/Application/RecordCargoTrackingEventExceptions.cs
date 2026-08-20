using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class CargoTrackingEventTransitionException(
    Guid cargoBookingId,
    CargoStatus currentStatus,
    CargoStatus requestedStatus)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' cannot move from status '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal CargoStatus CurrentStatus { get; } = currentStatus;

    internal CargoStatus RequestedStatus { get; } = requestedStatus;
}

internal sealed class RecordCargoTrackingEventValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record cargo tracking event validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class CargoSafetyInfoRequiredBeforeLoadingException(Guid cargoBookingId)
    : Exception(
        $"Hazardous cargo booking with ID '{cargoBookingId}' cannot be loaded before its safety information is declared.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}
