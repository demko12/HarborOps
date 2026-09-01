namespace HarborOps.Modules.Fleet.Contracts;

public sealed record UpdateVesselRegistryDetailsRequest
{
    public Guid? VesselTypeId { get; init; }

    public string? Name { get; init; }

    public string? Mmsi { get; init; }

    public string? CallSign { get; init; }

    public string? FlagState { get; init; }

    public string? PortOfRegistry { get; init; }

    public string? ClassificationSociety { get; init; }

    public string? LifecycleStage { get; init; }

    public string? MsmdId { get; init; }
}
