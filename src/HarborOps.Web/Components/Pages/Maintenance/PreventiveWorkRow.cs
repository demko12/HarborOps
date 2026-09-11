namespace HarborOps.Web.Components.Pages.Maintenance;

internal sealed record PreventiveWorkRow(
    Guid ScheduleId,
    Guid? OccurrenceId,
    Guid VesselId,
    string VesselDisplayName,
    string MaintenanceType,
    string Frequency,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    bool IsOverdue,
    Guid? WorkOrderId,
    int EquipmentCount);
