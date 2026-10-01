using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class PurchaseOrderClosureReader
{
    internal static async Task<PurchaseOrderClosureDetails> ReadDetailsAsync(
        DbContext dbContext,
        PurchaseOrderClosure closure,
        CancellationToken cancellationToken)
    {
        var lines = await dbContext.Set<PurchaseOrderClosureLine>()
            .AsNoTracking()
            .Where(line => line.PurchaseOrderClosureId == closure.Id)
            .ToListAsync(cancellationToken);

        var itemIds = lines.Select(line => line.InventoryItemId).Distinct().ToList();

        var items = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var units = await PurchaseOrderReceiptReader.ReadUnitsAsync(dbContext, items.Values, cancellationToken);

        var lineItems = lines
            .Select(line =>
            {
                var item = items.GetValueOrDefault(line.InventoryItemId);

                return new PurchaseOrderClosureLineItem(
                    line.Id,
                    line.PurchaseOrderLineId,
                    line.InventoryItemId,
                    item?.Name ?? string.Empty,
                    item?.PartNumber ?? string.Empty,
                    item is null ? string.Empty : units.GetValueOrDefault(item.UnitId, string.Empty),
                    line.QuantityOrdered,
                    line.ReceivedQuantity,
                    line.ReturnedQuantity,
                    line.ClosedQuantity);
            })
            .OrderBy(line => line.Name, StringComparer.Ordinal)
            .ThenBy(line => line.PartNumber, StringComparer.Ordinal)
            .ThenBy(line => line.PurchaseOrderLineId)
            .ToList();

        return new PurchaseOrderClosureDetails(
            closure.Id,
            closure.PurchaseOrderId,
            closure.ClientRequestId,
            closure.Reason,
            closure.ClosedByUserId,
            closure.ClosedUtc,
            closure.OrderStatusBefore.ToString(),
            closure.OrderStatusAfter.ToString(),
            lineItems.Sum(line => line.ReceivedQuantity),
            lineItems.Sum(line => line.ClosedQuantity),
            lineItems);
    }
}
