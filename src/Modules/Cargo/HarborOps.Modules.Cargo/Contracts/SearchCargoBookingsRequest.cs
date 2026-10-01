namespace HarborOps.Modules.Cargo.Contracts;

public sealed record SearchCargoBookingsRequest
{
    public string? Search { get; init; }

    public string? Status { get; init; }

    public bool? IsHazardous { get; init; }

    public bool? HasOpenDiscrepancies { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
