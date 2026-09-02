namespace HarborOps.Modules.Fleet.Contracts;

public sealed record UpdateVesselSpecificationsRequest
{
    public decimal? Dwt { get; init; }

    public decimal? Gt { get; init; }

    public decimal? Loa { get; init; }

    public decimal? Beam { get; init; }

    public decimal? Draft { get; init; }
}
