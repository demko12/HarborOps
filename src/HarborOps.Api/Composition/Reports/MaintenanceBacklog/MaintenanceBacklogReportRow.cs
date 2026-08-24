namespace HarborOps.Api.Composition.Reports.MaintenanceBacklog;

public sealed record MaintenanceBacklogReportRow(
    Guid WorkOrderId,
    Guid VesselId,
    string Title,
    string Priority,
    string Status,
    DateTime DueUtc,
    bool IsOverdue,
    Guid? AssignedEngineerId,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);
