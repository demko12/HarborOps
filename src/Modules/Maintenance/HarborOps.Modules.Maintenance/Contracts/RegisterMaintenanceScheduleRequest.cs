namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RegisterMaintenanceScheduleRequest
{
    public string? MaintenanceType { get; init; }

    public string? Frequency { get; init; }

    public DateOnly? NextDueDate { get; init; }

    public int? NextDueHours { get; init; }

    public IReadOnlyList<Guid>? EquipmentIds { get; init; }
}
