namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetDefectsRequest
{
    public string? Status { get; init; }

    public string? Severity { get; init; }

    public Guid? VesselId { get; init; }

    public int PageNumber { get; init; } = 1;

    public int PageSize { get; init; } = 25;
}
