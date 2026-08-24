namespace HarborOps.Modules.Voyages.Contracts;

public sealed record SearchRoutesRequest
{
    public string? Search { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
