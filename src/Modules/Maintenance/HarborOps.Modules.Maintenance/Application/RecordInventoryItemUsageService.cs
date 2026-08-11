using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public class RecordInventoryItemUsageService(DbContext dbContext)
{
    public async Task<RecordInventoryItemUsageResponse> RecordUsageAsync(
        Guid workOrderId,
        Guid partId,
        RecordInventoryItemUsageRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = RecordInventoryItemUsageValidator.Validate(request);
        if (errors.Count > 0)
        {
            throw new RecordInventoryItemUsageValidationException(errors);
        }

        var workOrder = await dbContext
            .Set<MaintenanceWorkOrder>()
            .SingleOrDefaultAsync(candidate => candidate.Id == workOrderId, cancellationToken);

        if (workOrder is null)
        {
            throw new MaintenanceWorkOrderNotFoundException(workOrderId);
        }

        var inventoryItem = await dbContext
            .Set<InventoryItem>()
            .SingleOrDefaultAsync(candidate => candidate.Id == partId, cancellationToken);

        if (inventoryItem is null)
        {
            throw new InventoryItemNotFoundException(partId);
        }

        if (workOrder.Status is not (WorkOrderStatus.Open or WorkOrderStatus.InProgress))
        {
            throw new RecordInventoryItemUsageConflictException(
                $"Work order '{workOrderId}' must be Open or InProgress to record part usage.");
        }

        var usageExists = await dbContext
            .Set<InventoryItemUsage>()
            .AnyAsync(
                usage => usage.WorkOrderId == workOrderId && usage.PartId == partId,
                cancellationToken);

        if (usageExists)
        {
            throw new RecordInventoryItemUsageConflictException(
                $"Usage for work order '{workOrderId}' and part '{partId}' already exists.");
        }

        var quantityUsed = request.QuantityUsed!.Value;

        try
        {
            inventoryItem.ReduceStock(quantityUsed);
        }
        catch (InvalidOperationException exception)
        {
            throw new RecordInventoryItemUsageConflictException(exception.Message);
        }

        var usage = InventoryItemUsage.Create(workOrderId, partId, quantityUsed);

        await dbContext.Set<InventoryItemUsage>().AddAsync(usage, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new RecordInventoryItemUsageResponse(
            workOrderId,
            partId,
            quantityUsed,
            inventoryItem.Quantity);
    }
}
