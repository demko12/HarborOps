namespace HarborOps.Modules.Fleet.Contracts;

public sealed record SearchVesselsRequest
{
    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }

    public string? Search { get; init; }

    public string? Status { get; init; }

    public Guid? FleetId { get; init; }
}
