namespace HarborOps.Modules.Voyages.Application;

internal sealed class RecordStopArrivalValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Record stop arrival validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class VoyageStopOutOfSequenceException(Guid voyageStopId, int stopOrder)
    : Exception(
        $"Voyage stop '{voyageStopId}' (stop {stopOrder}) cannot arrive before the previous stop "
        + "has departed.")
{
    internal Guid VoyageStopId { get; } = voyageStopId;

    internal int StopOrder { get; } = stopOrder;
}
