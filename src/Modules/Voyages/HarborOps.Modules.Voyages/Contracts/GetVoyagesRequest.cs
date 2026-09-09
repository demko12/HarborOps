namespace HarborOps.Modules.Voyages.Contracts;

public sealed record GetVoyagesRequest
{
    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    public string? Search { get; init; }

    public string? Status { get; init; }

    public bool? IncludeActive { get; init; }

    public bool? DelayImpactApplied { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
