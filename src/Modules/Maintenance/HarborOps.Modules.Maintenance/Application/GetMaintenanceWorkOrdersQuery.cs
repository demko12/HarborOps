using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetMaintenanceWorkOrdersQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetMaintenanceWorkOrdersResponse> GetWorkOrdersAsync(
        GetMaintenanceWorkOrdersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = GetMaintenanceWorkOrdersValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new GetMaintenanceWorkOrdersValidationException(errors);
        }

        var pageNumber = request.PageNumber ?? GetMaintenanceWorkOrdersValidator.DefaultPageNumber;
        var pageSize = request.PageSize ?? GetMaintenanceWorkOrdersValidator.DefaultPageSize;

        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var workOrders = dbContext.Set<MaintenanceWorkOrder>().AsNoTracking();

        if (request.VesselId.HasValue)
        {
            workOrders = workOrders.Where(workOrder => workOrder.VesselId == request.VesselId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = Enum.Parse<WorkOrderStatus>(request.Status, ignoreCase: true);
            workOrders = workOrders.Where(workOrder => workOrder.Status == status);
        }

        if (!string.IsNullOrEmpty(request.Priority))
        {
            var priority = Enum.Parse<WorkOrderPriority>(request.Priority, ignoreCase: true);
            workOrders = workOrders.Where(workOrder => workOrder.Priority == priority);
        }

        if (request.Overdue.HasValue)
        {
            if (request.Overdue.Value)
            {
                workOrders = workOrders.Where(workOrder =>
                    workOrder.DueUtc < utcNow
                    && (workOrder.Status == WorkOrderStatus.Open
                        || workOrder.Status == WorkOrderStatus.InProgress));
            }
            else
            {
                workOrders = workOrders.Where(workOrder =>
                    workOrder.DueUtc >= utcNow
                    || (workOrder.Status != WorkOrderStatus.Open
                        && workOrder.Status != WorkOrderStatus.InProgress));
            }
        }

        var totalCount = await workOrders.CountAsync(cancellationToken);

        var rows = await workOrders
            .OrderByDescending(workOrder => workOrder.Status == WorkOrderStatus.InProgress)
            .ThenBy(workOrder => (DateTime?)workOrder.DueUtc ?? DateTime.MaxValue)
            .ThenBy(workOrder => workOrder.Id)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(workOrder => new
            {
                workOrder.Id,
                workOrder.VesselId,
                workOrder.Title,
                workOrder.Description,
                workOrder.Priority,
                workOrder.Status,
                workOrder.DueUtc,
                workOrder.AssignedEngineerId,
                workOrder.CreatedUtc,
                workOrder.CompletedUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(row => new MaintenanceWorkOrderListItem(
                row.Id,
                row.VesselId,
                row.Title,
                row.Description,
                row.Priority.ToString(),
                row.Status.ToString(),
                row.DueUtc,
                row.AssignedEngineerId,
                row.CreatedUtc,
                row.CompletedUtc))
            .ToList();

        return new GetMaintenanceWorkOrdersResponse(items, pageNumber, pageSize, totalCount);
    }
}
