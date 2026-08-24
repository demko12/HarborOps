namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetDryDocksRequest
{
    public Guid? VesselId { get; init; }

    public Guid? PortId { get; init; }

    public string? Status { get; init; }

    public DateOnly? StartFrom { get; init; }

    public DateOnly? StartTo { get; init; }

    public bool? Overdue { get; init; }

    public string? Search { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
