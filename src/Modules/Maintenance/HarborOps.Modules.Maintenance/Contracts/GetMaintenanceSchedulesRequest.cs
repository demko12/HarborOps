namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetMaintenanceSchedulesRequest
{
    public Guid? VesselId { get; init; }

    public Guid? EquipmentId { get; init; }

    public DateOnly? DueFrom { get; init; }

    public DateOnly? DueTo { get; init; }

    public bool? Overdue { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
