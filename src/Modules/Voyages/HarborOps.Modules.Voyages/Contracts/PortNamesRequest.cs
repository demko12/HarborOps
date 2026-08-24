namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortNamesRequest
{
    public Guid[]? PortIds { get; init; }
}
