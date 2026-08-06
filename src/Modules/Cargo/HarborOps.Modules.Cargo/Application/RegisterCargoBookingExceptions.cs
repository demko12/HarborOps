namespace HarborOps.Modules.Cargo.Application;

internal sealed class DuplicateBookingNumberException(string bookingNumber)
    : Exception($"A cargo booking with booking number '{bookingNumber}' is already registered.")
{
    internal string BookingNumber { get; } = bookingNumber;
}

internal sealed class RegisterCargoBookingValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Register cargo booking validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
