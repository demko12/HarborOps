namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PlanVoyageItineraryRequest
{
    public Guid? RouteId { get; init; }

    public IReadOnlyList<PlanVoyageItineraryStop>? Stops { get; init; }
}
