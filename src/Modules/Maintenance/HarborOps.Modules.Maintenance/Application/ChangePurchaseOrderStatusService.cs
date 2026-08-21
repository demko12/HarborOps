using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

public sealed class ChangePurchaseOrderStatusService(DbContext dbContext)
{
    public Task<ChangePurchaseOrderStatusResponse> MarkOrderedAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(purchaseOrderId, PurchaseOrderStatus.Ordered, cancellationToken);
    }

    public Task<ChangePurchaseOrderStatusResponse> ReceiveAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(purchaseOrderId, PurchaseOrderStatus.Received, cancellationToken);
    }

    public Task<ChangePurchaseOrderStatusResponse> CancelAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(purchaseOrderId, PurchaseOrderStatus.Cancelled, cancellationToken);
    }

    private async Task<ChangePurchaseOrderStatusResponse> ChangeStatusAsync(
        Guid purchaseOrderId,
        PurchaseOrderStatus targetStatus,
        CancellationToken cancellationToken)
    {
        var purchaseOrder = await dbContext.Set<PurchaseOrder>()
            .FirstOrDefaultAsync(order => order.Id == purchaseOrderId, cancellationToken);

        if (purchaseOrder is null)
        {
            throw new PurchaseOrderNotFoundException(purchaseOrderId);
        }

        var previousStatus = purchaseOrder.Status;

        if (!PurchaseOrder.CanTransition(previousStatus, targetStatus))
        {
            throw new PurchaseOrderConflictException(
                $"Purchase order '{purchaseOrderId}' cannot move from status '{previousStatus}' to '{targetStatus}'.");
        }

        var stockAdjustments = targetStatus == PurchaseOrderStatus.Received
            ? await ApplyReceiptAsync(purchaseOrder, cancellationToken)
            : [];

        purchaseOrder.ChangeStatusTo(targetStatus);

        // One SaveChangesAsync: the status change and every stock adjustment commit together.
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ChangePurchaseOrderStatusResponse(
            purchaseOrder.Id,
            previousStatus.ToString(),
            purchaseOrder.Status.ToString(),
            PurchaseOrder.AllowedTransitions(purchaseOrder.Status)
                .Select(status => status.ToString())
                .ToList(),
            stockAdjustments);
    }

    private async Task<IReadOnlyList<PurchaseOrderStockAdjustment>> ApplyReceiptAsync(
        PurchaseOrder purchaseOrder,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderId == purchaseOrder.Id)
            .ToListAsync(cancellationToken);

        if (lines.Count == 0)
        {
            throw new PurchaseOrderConflictException(
                $"Purchase order '{purchaseOrder.Id}' has no lines to receive.");
        }

        var quantitiesByItem = lines
            .GroupBy(line => line.PartId)
            .ToDictionary(group => group.Key, group => group.Sum(line => line.QuantityOrdered));

        var itemIds = quantitiesByItem.Keys.ToList();

        var inventoryItems = await dbContext.Set<InventoryItem>()
            .Where(item => itemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var missingItemIds = itemIds
            .Where(itemId => inventoryItems.TrueForAll(item => item.Id != itemId))
            .ToList();

        if (missingItemIds.Count > 0)
        {
            throw new PurchaseOrderItemsNotFoundException(missingItemIds);
        }

        var adjustments = new List<PurchaseOrderStockAdjustment>(inventoryItems.Count);

        foreach (var inventoryItem in inventoryItems.OrderBy(item => item.Id))
        {
            var quantityReceived = quantitiesByItem[inventoryItem.Id];

            inventoryItem.IncreaseStock(quantityReceived);

            adjustments.Add(new PurchaseOrderStockAdjustment(
                inventoryItem.Id,
                quantityReceived,
                inventoryItem.Quantity));
        }

        return adjustments;
    }
}
