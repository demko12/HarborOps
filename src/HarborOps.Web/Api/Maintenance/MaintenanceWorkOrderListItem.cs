namespace HarborOps.Web.Api.Maintenance;

internal sealed record MaintenanceWorkOrderListItem(
    Guid WorkOrderId,
    Guid VesselId,
    string Title,
    string? Description,
    string Priority,
    string Status,
    DateTime DueUtc,
    Guid? AssignedEngineerId,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);
