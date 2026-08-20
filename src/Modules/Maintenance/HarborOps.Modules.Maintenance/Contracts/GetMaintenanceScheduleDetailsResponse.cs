namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetMaintenanceScheduleDetailsResponse(
    Guid ScheduleId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours,
    bool IsOverdue,
    int? DaysUntilDue,
    IReadOnlyList<MaintenanceScheduleEquipmentItem> Equipment);
