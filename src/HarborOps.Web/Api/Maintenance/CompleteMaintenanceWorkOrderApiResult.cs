namespace HarborOps.Web.Api.Maintenance;

internal sealed record CompleteMaintenanceWorkOrderApiResult(
    Guid WorkOrderId,
    Guid MaintenanceRecordId,
    DateTime CompletedUtc);
