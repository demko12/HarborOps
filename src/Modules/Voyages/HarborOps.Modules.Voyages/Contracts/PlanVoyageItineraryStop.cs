namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PlanVoyageItineraryStop
{
    public int? StopOrder { get; init; }

    public Guid? PortId { get; init; }

    public decimal? DistanceFromPrevious { get; init; }

    public int? EstimatedDurationFromPrevious { get; init; }

    public DateTime? Eta { get; init; }

    public DateTime? Etd { get; init; }
}
