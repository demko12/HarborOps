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

        // The work order, its equipment, part usages and latest completion record are loaded in a single
        // round trip; separate queries cost one database round trip each.
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
                Equipment = (
                    from link in dbContext.Set<WorkOrderEquipment>()
                    join equipment in dbContext.Set<Equipment>()
                        on link.EquipmentId equals equipment.Id
                    where link.WorkOrderId == item.Id
                    orderby equipment.Name, equipment.Id
                    select new WorkOrderEquipmentListItem(
                        equipment.Id,
                        equipment.VesselId,
                        equipment.Name,
                        equipment.Category,
                        equipment.Manufacturer,
                        equipment.SerialNumber))
                    .ToList(),
                PartUsages = (
                    from usage in dbContext.Set<InventoryItemUsage>()
                    join part in dbContext.Set<InventoryItem>()
                        on usage.PartId equals part.Id
                    where usage.WorkOrderId == item.Id
                    orderby part.Name, part.PartNumber, part.Id
                    select new WorkOrderPartUsageListItem(
                        usage.PartId,
                        part.Name,
                        part.PartNumber,
                        usage.QuantityUsed,
                        part.Quantity))
                    .ToList(),
                // Loaded as a collection so SQL Server seeks IX_MaintenanceRecords_WorkOrderId; FirstOrDefault
                // here would be translated to ROW_NUMBER() over the whole MaintenanceRecords table.
                Completions = dbContext.Set<MaintenanceRecord>()
                    .Where(record => record.WorkOrderId == item.Id)
                    .OrderByDescending(record => record.CompletionDate)
                    .ThenBy(record => record.Id)
                    .Select(record => new WorkOrderCompletionRecordItem(
                        record.Id,
                        record.TechnicianId,
                        record.CompletionDate,
                        record.Notes,
                        record.Cost,
                        record.LaborHours))
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

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
            workOrder.Equipment,
            workOrder.PartUsages,
            workOrder.Completions.FirstOrDefault());
    }
}
