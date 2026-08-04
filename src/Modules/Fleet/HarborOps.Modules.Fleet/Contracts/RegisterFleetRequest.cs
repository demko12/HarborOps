namespace HarborOps.Modules.Fleet.Contracts;

public sealed record RegisterFleetRequest
{
    public string? Name { get; init; }

    public string? Description { get; init; }
}
