namespace HarborOps.Modules.Documents.Contracts;

public sealed record GetVesselReadinessOverviewRequest
{
    public DateOnly? RequiredThrough { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
