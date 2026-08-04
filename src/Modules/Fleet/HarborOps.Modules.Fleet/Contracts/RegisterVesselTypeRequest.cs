namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RegisterVesselTypeRequest
{
    public string? Name { get; init; }

    public string? Code { get; init; }

    public string? Description { get; init; }
}
