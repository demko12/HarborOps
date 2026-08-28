namespace HarborOps.Modules.Voyages.Application;

internal sealed class RecordStopDepartureValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record stop departure validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
