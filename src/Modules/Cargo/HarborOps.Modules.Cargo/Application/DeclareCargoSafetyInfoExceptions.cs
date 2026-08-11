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

internal sealed class CargoSafetyInfoAlreadyDeclaredException(Guid cargoBookingId)
    : Exception($"Cargo booking with ID '{cargoBookingId}' already has safety information declared.")
{
    internal Guid CargoBookingId { get; } = cargoBookingId;
}

internal sealed class DeclareCargoSafetyInfoValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Declare cargo safety information validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
