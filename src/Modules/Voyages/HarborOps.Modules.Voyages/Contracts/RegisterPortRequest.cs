namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RegisterPortRequest
{
    public string? Name { get; init; }

    public string? Country { get; init; }

    public string? City { get; init; }

    public string? UnLocode { get; init; }

    public decimal? Latitude { get; init; }

    public decimal? Longitude { get; init; }

    public string? ShipyardName { get; init; }
}
