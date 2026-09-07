namespace HarborOps.Modules.Crew.Contracts;

public sealed record SearchCrewPositionsRequest
{
    public string? Search { get; init; }

    public string? Department { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
