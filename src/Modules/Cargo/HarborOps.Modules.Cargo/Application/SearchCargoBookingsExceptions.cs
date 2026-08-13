namespace HarborOps.Modules.Cargo.Application;

internal sealed class SearchCargoBookingsValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Search cargo bookings validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
