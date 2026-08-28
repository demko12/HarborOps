namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PlanVoyageItineraryResponse(
    Guid VoyageId,
    IReadOnlyList<PlannedVoyageStopItem> Stops);
