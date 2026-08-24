using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class ReportsMaintenanceBacklogQuery(DbContext dbContext) : IReportsMaintenanceBacklogQuery
{
    internal const int MaxPageSize = 200;

    public async Task<ReportsMaintenanceBacklogResult> SearchAsync(
        ReportsMaintenanceBacklogRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var pageNumber = Math.Max(1, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var asOfUtc = request.AsOf.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var workOrders = dbContext.Set<MaintenanceWorkOrder>().AsNoTracking();

        if (request.VesselId is { } vesselId)
        {
            workOrders = workOrders.Where(workOrder => workOrder.VesselId == vesselId);
        }

        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            var priority = Enum.Parse<WorkOrderPriority>(request.Priority, ignoreCase: true);
            workOrders = workOrders.Where(workOrder => workOrder.Priority == priority);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = Enum.Parse<WorkOrderStatus>(request.Status, ignoreCase: true);
            workOrders = workOrders.Where(workOrder => workOrder.Status == status);
        }

        if (request.DueFrom is { } dueFrom)
        {
            var dueFromUtc = dueFrom.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            workOrders = workOrders.Where(workOrder => workOrder.DueUtc >= dueFromUtc);
        }

        if (request.DueTo is { } dueTo)
        {
            var dueToUtcExclusive = dueTo.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            workOrders = workOrders.Where(workOrder => workOrder.DueUtc < dueToUtcExclusive);
        }

        if (request.Overdue is { } overdue)
        {
            workOrders = overdue
                ? workOrders.Where(workOrder =>
                    workOrder.DueUtc < asOfUtc
                    && (workOrder.Status == WorkOrderStatus.Open || workOrder.Status == WorkOrderStatus.InProgress))
                : workOrders.Where(workOrder =>
                    workOrder.DueUtc >= asOfUtc
                    || (workOrder.Status != WorkOrderStatus.Open && workOrder.Status != WorkOrderStatus.InProgress));
        }

        var totalCount = await workOrders.CountAsync(cancellationToken);

        var rows = await workOrders
            .OrderBy(workOrder => workOrder.DueUtc)
            .ThenBy(workOrder => workOrder.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(workOrder => new
            {
                workOrder.Id,
                workOrder.VesselId,
                workOrder.Title,
                workOrder.Priority,
                workOrder.Status,
                workOrder.DueUtc,
                workOrder.AssignedEngineerId,
                workOrder.CreatedUtc,
                workOrder.CompletedUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new ReportsMaintenanceBacklogRow(
                row.Id,
                row.VesselId,
                row.Title,
                row.Priority.ToString(),
                row.Status.ToString(),
                row.DueUtc,
                row.DueUtc < asOfUtc && row.Status is WorkOrderStatus.Open or WorkOrderStatus.InProgress,
                row.AssignedEngineerId,
                row.CreatedUtc,
                row.CompletedUtc))
            .ToList();

        return new ReportsMaintenanceBacklogResult(items, pageNumber, pageSize, totalCount);
    }
}
