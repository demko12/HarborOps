namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageItineraryResponse(
    Guid VoyageId,
    IReadOnlyList<VoyageItineraryStopItem> Stops,
    IReadOnlyList<VoyageDelayItem> Delays);
