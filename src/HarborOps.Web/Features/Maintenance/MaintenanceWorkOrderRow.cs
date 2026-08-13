namespace HarborOps.Web.Features.Maintenance;

internal sealed record MaintenanceWorkOrderRow(
    Guid WorkOrderId,
    Guid VesselId,
    string VesselDisplayName,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime DueUtc,
    Guid? AssignedEngineerId,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    bool IsOverdue,
    bool IsCritical,
    bool CanComplete);
