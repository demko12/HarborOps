namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RegisterRouteRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public decimal? EstimatedDistance { get; init; }

    public int? EstimatedDuration { get; init; }

    public IReadOnlyList<RegisterRouteStop>? Stops { get; init; }
}
