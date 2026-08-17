namespace HarborOps.Modules.Maintenance.Contracts;

public interface IOperationsDashboardMaintenanceQuery
{
    Task<OperationsDashboardMaintenanceResult> GetAttentionWorkOrdersAsync(
        DateTime asOfUtc,
        int maximumItems,
        CancellationToken cancellationToken);
}

public sealed record OperationsDashboardMaintenanceResult(
    int AttentionWorkOrderCount,
    IReadOnlyList<OperationsDashboardMaintenanceItem> Items);

public sealed record OperationsDashboardMaintenanceItem(
    Guid WorkOrderId,
    string Title,
    string Priority,
    string Status,
    DateTime DueUtc,
    bool IsOverdue);
