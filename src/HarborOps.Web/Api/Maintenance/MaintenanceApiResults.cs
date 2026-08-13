namespace HarborOps.Web.Api.Maintenance;

internal sealed record GetWorkOrdersResult(
    MaintenanceRequestStatus Status,
    MaintenanceWorkOrdersPage? Page,
    IReadOnlyDictionary<string, string[]>? ValidationErrors,
    string? ErrorMessage);

internal sealed record CreateWorkOrderResult(
    MaintenanceRequestStatus Status,
    Guid? WorkOrderId,
    IReadOnlyDictionary<string, string[]>? ValidationErrors,
    string? ErrorMessage);

internal sealed record CompleteWorkOrderResult(
    MaintenanceRequestStatus Status,
    DateTime? CompletedUtc,
    IReadOnlyDictionary<string, string[]>? ValidationErrors,
    string? ErrorMessage);
