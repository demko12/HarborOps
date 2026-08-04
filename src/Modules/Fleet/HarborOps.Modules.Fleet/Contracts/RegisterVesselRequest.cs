namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RegisterVesselRequest
{
    public Guid? CompanyId { get; init; }

    public Guid? VesselTypeId { get; init; }

    public string? Name { get; init; }

    public string? ImoNumber { get; init; }

    public string? FlagState { get; init; }

    public string? CurrentStatus { get; init; }
}
