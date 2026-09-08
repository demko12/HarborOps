using HarborOps.Modules.Cargo.Domain;

namespace HarborOps.Modules.Cargo.Application;

internal sealed class CargoBookingNotFoundException(Guid cargoBookingId)
    : Exception($"Cargo booking with ID '{cargoBookingId}' was not found.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class CargoBookingNotHazardousException(Guid cargoBookingId)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' is not hazardous and cannot receive safety information.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class CargoSafetyInfoNotFoundException(Guid cargoBookingId)
    : Exception(
        $"Cargo booking with ID '{cargoBookingId}' does not have safety information declared.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class CargoSafetyInfoTransitionException(
    Guid cargoSafetyInfoId,
    CargoSafetyReviewStatus currentStatus,
    string requestedAction)
    : Exception(
        $"Cargo safety information with ID '{cargoSafetyInfoId}' cannot be {requestedAction} while it is '{currentStatus}'.")
{
    internal Guid CargoSafetyInfoId { get; } = cargoSafetyInfoId;

    internal CargoSafetyReviewStatus CurrentStatus { get; } = currentStatus;

    internal string RequestedAction { get; } = requestedAction;
}

internal sealed class CargoSafetyInfoValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Cargo safety information validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
internal sealed class CargoSafetyApprovalRequiredException(
    Guid cargoBookingId,
    CargoSafetyReviewStatus? reviewStatus)
    : Exception(
        $"Hazardous cargo booking with ID '{cargoBookingId}' cannot be loaded until its safety declaration is approved.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;

    internal CargoSafetyReviewStatus? ReviewStatus { get; } = reviewStatus;
}
