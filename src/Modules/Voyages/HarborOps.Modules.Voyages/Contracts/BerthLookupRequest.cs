namespace HarborOps.Modules.Voyages.Contracts;

public sealed record BerthLookupRequest
{
    public Guid? PortId { get; init; }

    public string? Search { get; init; }

    public int? Take { get; init; }
}
