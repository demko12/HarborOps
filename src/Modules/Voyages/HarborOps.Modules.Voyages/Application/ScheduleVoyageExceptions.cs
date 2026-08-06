namespace HarborOps.Modules.Voyages.Application;

internal sealed class DuplicateVoyageNumberException(string voyageNumber)
    : Exception($"A voyage with voyage number '{voyageNumber}' is already scheduled.")
{
    internal string VoyageNumber { get; } = voyageNumber;
}

internal sealed class ScheduleVoyageValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Schedule voyage validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
