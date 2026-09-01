using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetMaintenanceWorkOrderDetailsQuery(DbContext dbContext, TimeProvider timeProvider)
{
    public async Task<GetMaintenanceWorkOrderDetailsResponse> GetDetailsAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var utcNow = timeProvider.GetUtcNow().UtcDateTime;

        var workOrder = await dbContext.Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .Where(item => item.Id == workOrderId)
            .Select(item => new
            {
                item.Id,
                item.VesselId,
                item.DryDockId,
                item.AssignedEngineerId,
                item.Title,
                item.Description,
                item.Priority,
                item.Status,
                item.DueUtc,
                item.CreatedUtc,
                item.CompletedUtc,
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        var links = dbContext.Set<WorkOrderEquipment>().AsNoTracking();

        var equipment = await dbContext.Set<Equipment>()
            .AsNoTracking()
            .Where(item => links.Any(link => link.WorkOrderId == workOrderId && link.EquipmentId == item.Id))
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .Select(item => new WorkOrderEquipmentListItem(
                item.Id,
                item.VesselId,
                item.Name,
                item.Category,
                item.Manufacturer,
                item.SerialNumber))
            .ToListAsync(cancellationToken);

        var partUsages = await (
            from usage in dbContext.Set<InventoryItemUsage>().AsNoTracking()
            join item in dbContext.Set<InventoryItem>().AsNoTracking()
                on usage.PartId equals item.Id
            where usage.WorkOrderId == workOrderId
            orderby item.Name, item.PartNumber, item.Id
            select new WorkOrderPartUsageListItem(
                usage.PartId,
                item.Name,
                item.PartNumber,
                usage.QuantityUsed,
                item.Quantity))
            .ToListAsync(cancellationToken);

        var completion = await dbContext.Set<MaintenanceRecord>()
            .AsNoTracking()
            .Where(record => record.WorkOrderId == workOrderId)
            .OrderByDescending(record => record.CompletionDate)
            .ThenBy(record => record.Id)
            .Select(record => new WorkOrderCompletionRecordItem(
                record.Id,
                record.TechnicianId,
                record.CompletionDate,
                record.Notes,
                record.Cost,
                record.LaborHours))
            .FirstOrDefaultAsync(cancellationToken);

        return new GetMaintenanceWorkOrderDetailsResponse(
            workOrder.Id,
            workOrder.VesselId,
            workOrder.DryDockId,
            workOrder.AssignedEngineerId,
            workOrder.Title,
            workOrder.Description,
            workOrder.Priority.ToString(),
            workOrder.Status.ToString(),
            workOrder.DueUtc,
            workOrder.CreatedUtc,
            workOrder.CompletedUtc,
            MaintenanceWorkOrder.IsOverdue(workOrder.Status, workOrder.DueUtc, utcNow),
            MaintenanceWorkOrder.AllowedActionsFor(workOrder.Status)
                .Select(action => action.ToString())
                .ToList(),
            equipment,
            partUsages,
            completion);
    }
}
