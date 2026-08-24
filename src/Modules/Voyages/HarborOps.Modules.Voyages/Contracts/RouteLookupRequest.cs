namespace HarborOps.Modules.Voyages.Contracts;

public sealed record RouteLookupRequest
{
    public string? Search { get; init; }

    public int? Take { get; init; }
}
