namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RegisterRouteStop
{
    public Guid? PortId { get; init; }

    public int? StopOrder { get; init; }

    public decimal? DistanceFromPrevious { get; init; }

    public int? EstimatedDurationFromPrevious { get; init; }
}
