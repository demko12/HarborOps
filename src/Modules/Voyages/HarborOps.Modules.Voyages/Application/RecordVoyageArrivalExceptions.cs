namespace HarborOps.Modules.Voyages.Application;

internal sealed class RecordVoyageArrivalValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record voyage arrival validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
