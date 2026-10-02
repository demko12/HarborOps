using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class PurchaseOrderReceiptReader
{
    internal static IReadOnlyList<string> AllowedActions(PurchaseOrderStatus status)
    {
        return status switch
        {
            PurchaseOrderStatus.Draft =>
                [PurchaseOrderActions.MarkOrdered, PurchaseOrderActions.Cancel],
            PurchaseOrderStatus.Ordered =>
                [PurchaseOrderActions.ReceivePartial, PurchaseOrderActions.ReceiveRemaining, PurchaseOrderActions.Cancel],
            PurchaseOrderStatus.PartiallyReceived =>
                [
                    PurchaseOrderActions.ReceivePartial,
                    PurchaseOrderActions.ReceiveRemaining,
                    PurchaseOrderActions.CloseRemaining,
                    PurchaseOrderActions.CreateReturn,
                ],
            PurchaseOrderStatus.Received or PurchaseOrderStatus.ClosedShort =>
                [PurchaseOrderActions.CreateReturn],
            _ => [],
        };
    }

    // Loads every inventory item the lines refer to; a missing item means the order cannot be received.
    internal static async Task<Dictionary<Guid, InventoryItem>> ReadItemsAsync(
        DbContext dbContext,
        IEnumerable<PurchaseOrderLine> lines,
        bool track,
        CancellationToken cancellationToken)
    {
        var itemIds = lines.Select(line => line.PartId).Distinct().ToList();

        var query = dbContext.Set<InventoryItem>().Where(item => itemIds.Contains(item.Id));

        var items = await (track ? query : query.AsNoTracking())
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var missingItemIds = itemIds.Where(itemId => !items.ContainsKey(itemId)).ToList();

        if (missingItemIds.Count > 0)
        {
            throw new PurchaseOrderItemsNotFoundException(missingItemIds);
        }

        return items;
    }

    internal static async Task<Dictionary<Guid, string>> ReadUnitsAsync(
        DbContext dbContext,
        IEnumerable<InventoryItem> items,
        CancellationToken cancellationToken)
    {
        var unitIds = items.Select(item => item.UnitId).Distinct().ToList();

        return await dbContext.Set<UnitOfMeasure>()
            .AsNoTracking()
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Abbreviation, cancellationToken);
    }

    internal static async Task<IReadOnlyList<PurchaseOrderReceiptDetails>> ReadDetailsAsync(
        DbContext dbContext,
        IReadOnlyList<PurchaseOrderReceipt> receipts,
        CancellationToken cancellationToken)
    {
        if (receipts.Count == 0)
        {
            return [];
        }

        var receiptIds = receipts.Select(receipt => receipt.Id).ToList();

        var lines = await dbContext.Set<PurchaseOrderReceiptLine>()
            .AsNoTracking()
            .Where(line => receiptIds.Contains(line.PurchaseOrderReceiptId))
            .ToListAsync(cancellationToken);

        var purchaseOrderLineIds = lines.Select(line => line.PurchaseOrderLineId).Distinct().ToList();

        var orderedQuantities = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => purchaseOrderLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, line => line.QuantityOrdered, cancellationToken);

        var itemIds = lines.Select(line => line.InventoryItemId).Distinct().ToList();

        var items = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var units = await ReadUnitsAsync(dbContext, items.Values, cancellationToken);
        var movements = await ReadMovementsAsync(dbContext, receiptIds, cancellationToken);

        var linesByReceipt = lines.ToLookup(line => line.PurchaseOrderReceiptId);
        var movementsByReceipt = movements.ToLookup(movement => movement.PurchaseOrderReceiptId);

        return receipts
            .Select(receipt =>
            {
                var lineItems = linesByReceipt[receipt.Id]
                    .Select(line =>
                    {
                        var item = items.GetValueOrDefault(line.InventoryItemId);

                        return new PurchaseOrderReceiptLineItem(
                            line.Id,
                            line.PurchaseOrderLineId,
                            line.InventoryItemId,
                            item?.Name ?? string.Empty,
                            item?.PartNumber ?? string.Empty,
                            item is null ? string.Empty : units.GetValueOrDefault(item.UnitId, string.Empty),
                            line.Quantity,
                            line.LineReceivedQuantityAfter,
                            orderedQuantities.GetValueOrDefault(line.PurchaseOrderLineId) - line.LineReceivedQuantityAfter);
                    })
                    .OrderBy(line => line.Name, StringComparer.Ordinal)
                    .ThenBy(line => line.PartNumber, StringComparer.Ordinal)
                    .ThenBy(line => line.PurchaseOrderLineId)
                    .ToList();

                return new PurchaseOrderReceiptDetails(
                    receipt.Id,
                    receipt.PurchaseOrderId,
                    receipt.ClientRequestId,
                    receipt.ReceivedUtc,
                    receipt.DeliveryReference,
                    receipt.Notes,
                    receipt.RecordedByUserId,
                    receipt.RecordedUtc,
                    receipt.OrderStatusBefore.ToString(),
                    receipt.OrderStatusAfter.ToString(),
                    lineItems.Sum(line => line.Quantity),
                    lineItems,
                    movementsByReceipt[receipt.Id].ToList());
            })
            .ToList();
    }

    internal static async Task<IReadOnlyList<PurchaseOrderReceiptMovementItem>> ReadMovementsAsync(
        DbContext dbContext,
        IReadOnlyCollection<Guid> receiptIds,
        CancellationToken cancellationToken)
    {
        var ids = receiptIds.ToList();

        return await dbContext.Set<InventoryAdjustment>()
            .AsNoTracking()
            .Where(adjustment => adjustment.PurchaseOrderReceiptId != null
                && ids.Contains(adjustment.PurchaseOrderReceiptId.Value))
            .OrderBy(adjustment => adjustment.InventoryItemId)
            .ThenBy(adjustment => adjustment.QuantityBefore)
            .ThenBy(adjustment => adjustment.Id)
            .Select(adjustment => new PurchaseOrderReceiptMovementItem(
                adjustment.Id,
                adjustment.InventoryItemId,
                adjustment.PurchaseOrderReceiptId!.Value,
                adjustment.PurchaseOrderReceiptLineId!.Value,
                adjustment.QuantityBefore,
                adjustment.QuantityAfter,
                adjustment.QuantityDelta,
                adjustment.Reason,
                adjustment.ActorUserId,
                adjustment.AdjustedUtc))
            .ToListAsync(cancellationToken);
    }
}
