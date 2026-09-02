namespace HarborOps.Modules.Voyages.Application;

internal sealed class PlanVoyageItineraryValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Plan voyage itinerary validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class VoyageItineraryNotPlannableException(Guid voyageId, string currentStatus)
    : Exception(
        $"Voyage '{voyageId}' cannot receive an itinerary while its status is '{currentStatus}'.")
{
    internal Guid VoyageId { get; } = voyageId;

    internal string CurrentStatus { get; } = currentStatus;
}

internal sealed class VoyageItineraryAlreadyPlannedException(Guid voyageId)
    : Exception($"Voyage '{voyageId}' already has an itinerary.")
{
    internal Guid VoyageId { get; } = voyageId;
}

internal sealed class VoyageStopPortNotFoundException(Guid portId)
    : Exception($"Voyage stop port '{portId}' was not found.")
{
    internal Guid PortId { get; } = portId;
}
