using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class GetWorkOrderPartUsagesQuery(DbContext dbContext)
{
    public async Task<GetWorkOrderPartUsagesResponse> GetPartUsagesAsync(
        Guid workOrderId,
        CancellationToken cancellationToken)
    {
        var workOrderExists = await dbContext
            .Set<MaintenanceWorkOrder>()
            .AsNoTracking()
            .AnyAsync(workOrder => workOrder.Id == workOrderId, cancellationToken);

        if (!workOrderExists)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        var rows = await(
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

        return new GetWorkOrderPartUsagesResponse(rows);
    }
}
