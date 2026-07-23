namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RegisterVesselRequest
{
    public string? Name { get; init; }

    public string? ImoNumber { get; init; }

    public string? FlagState { get; init; }

    public string? VesselType { get; init; }
}
