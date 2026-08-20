namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record MaintenanceScheduleListItem(
    Guid ScheduleId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours,
    bool IsOverdue,
    int? DaysUntilDue,
    int EquipmentCount);
