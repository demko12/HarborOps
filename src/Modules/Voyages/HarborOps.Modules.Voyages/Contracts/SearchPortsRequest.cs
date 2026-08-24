namespace HarborOps.Modules.Voyages.Contracts;

public sealed record SearchPortsRequest
{
    public string? Search { get; init; }

    public string? Country { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
