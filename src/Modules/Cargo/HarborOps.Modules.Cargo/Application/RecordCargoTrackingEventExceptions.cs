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

