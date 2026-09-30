using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

// Receiving is not a plain status change: it posts a receipt through PostPurchaseOrderReceiptService.
public sealed class ChangePurchaseOrderStatusService(DbContext dbContext)
{
    public Task<ChangePurchaseOrderStatusResponse> MarkOrderedAsync(
        Guid purchaseOrderId,
        CancellationToken cancellationToken)
    {
        return ChangeStatusAsync(purchaseOrderId, PurchaseOrderStatus.Ordered, cancellationToken);
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

        // Once any goods have been received the order can no longer be cancelled.
        if (!PurchaseOrder.CanTransition(previousStatus, targetStatus))
        {
            throw new PurchaseOrderConflictException(
                $"Purchase order '{purchaseOrderId}' cannot move from status '{previousStatus}' to '{targetStatus}'.");
        }

        purchaseOrder.ChangeStatusTo(targetStatus);

        // The order's concurrency token makes a cancellation racing a receipt fail instead of both winning.
        await dbContext.SaveChangesAsync(cancellationToken);

        return new ChangePurchaseOrderStatusResponse(
            purchaseOrder.Id,
            previousStatus.ToString(),
            purchaseOrder.Status.ToString(),
            PurchaseOrder.AllowedTransitions(purchaseOrder.Status)
                .Select(status => status.ToString())
                .ToList(),
            [],
            null,
            false);
    }
}
