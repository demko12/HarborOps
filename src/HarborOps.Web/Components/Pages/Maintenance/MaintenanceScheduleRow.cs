namespace HarborOps.Web.Components.Pages.Maintenance;

internal sealed record MaintenanceScheduleRow(
    Guid ScheduleId,
    Guid VesselId,
    string VesselDisplayName,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    int? NextDueHours,
    bool IsOverdue,
    int? DaysUntilDue,
    int EquipmentCount);
