using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class OperationsDashboardMaintenanceQuery(DbContext dbContext)
    : IOperationsDashboardMaintenanceQuery
{
    public async Task<OperationsDashboardMaintenanceResult> GetAttentionWorkOrdersAsync(
        DateTime asOfUtc,
        int maximumItems,
        CancellationToken cancellationToken)
    {
        var workOrders = dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .Where(workOrder =>
                (workOrder.Status == WorkOrderStatus.Open || workOrder.Status == WorkOrderStatus.InProgress)
                && (workOrder.DueUtc < asOfUtc || workOrder.Priority == WorkOrderPriority.Critical));

        var attentionWorkOrderCount = await workOrders.CountAsync(cancellationToken);
        var items = await workOrders
            .OrderByDescending(workOrder => workOrder.Priority == WorkOrderPriority.Critical)
            .ThenBy(workOrder => workOrder.DueUtc)
            .ThenBy(workOrder => workOrder.Id)
            .Take(maximumItems)
            .Select(workOrder => new OperationsDashboardMaintenanceItem(
                workOrder.Id,
                workOrder.Title,
                workOrder.Priority.ToString(),
                workOrder.Status.ToString(),
                workOrder.DueUtc,
                workOrder.DueUtc < asOfUtc))
            .ToListAsync(cancellationToken);

        return new OperationsDashboardMaintenanceResult(attentionWorkOrderCount, items);
    }
}
