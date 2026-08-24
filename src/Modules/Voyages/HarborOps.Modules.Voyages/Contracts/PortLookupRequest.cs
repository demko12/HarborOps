namespace HarborOps.Modules.Voyages.Contracts;

public sealed record PortLookupRequest
{
    public string? Search { get; init; }

    public int? Take { get; init; }
}
