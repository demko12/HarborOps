namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortLookupRequest
{
    public IReadOnlyCollection<Guid>? PortIds { get; init; }
}
