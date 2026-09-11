namespace HarborOps.Modules.Voyages.Contracts;

public sealed record VoyageItineraryResponse(
    Guid VoyageId,
    Guid VesselId,
    IReadOnlyList<VoyageItineraryStopItem> Stops,
    IReadOnlyList<VoyageDelayItem> Delays);
