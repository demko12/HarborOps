using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

internal static class SupplierReturnPostingEvaluator
{
    internal sealed record Evaluation(
        IReadOnlyList<SupplierReturnLineItem> Lines,
        IReadOnlyList<SupplierReturnPreviewItem> Items,
        IReadOnlyList<SupplierReturnPostingBlocker> Blockers,
        string PreviewToken);

    internal static async Task<Evaluation> EvaluateAsync(
        DbContext dbContext,
        SupplierReturn supplierReturn,
        PurchaseOrderStatus purchaseOrderStatus,
        IReadOnlyList<SupplierReturnLine> lines,
        IReadOnlyDictionary<Guid, InventoryItem> items,
        CancellationToken cancellationToken)
    {
        var purchaseOrderLineIds = lines.Select(line => line.PurchaseOrderLineId).ToList();
        var itemIds = lines.Select(line => line.InventoryItemId).Distinct().ToList();

        var receivedQuantities = await dbContext.Set<PurchaseOrderLine>()
            .AsNoTracking()
            .Where(line => purchaseOrderLineIds.Contains(line.Id))
            .ToDictionaryAsync(line => line.Id, line => line.QuantityOrdered, cancellationToken);

        var postedQuantities = await SupplierReturnReader.ReadPostedQuantitiesAsync(
            dbContext,
            purchaseOrderLineIds,
            supplierReturn.Id,
            cancellationToken);

        var holds = await (
            from reservation in dbContext.Set<WorkOrderPartReservation>().AsNoTracking()
            join workOrder in dbContext.Set<MaintenanceWorkOrder>().AsNoTracking()
                on reservation.WorkOrderId equals workOrder.Id
            where itemIds.Contains(reservation.InventoryItemId)
                && (workOrder.Status == WorkOrderStatus.Open || workOrder.Status == WorkOrderStatus.InProgress)
                && reservation.QuantityReserved > reservation.QuantityConsumed
            select new
            {
                reservation.InventoryItemId,
                reservation.WorkOrderId,
                Held = reservation.QuantityReserved - reservation.QuantityConsumed,
            })
            .ToListAsync(cancellationToken);

        var unitIds = items.Values.Select(item => item.UnitId).Distinct().ToList();

        var units = await dbContext.Set<UnitOfMeasure>()
            .AsNoTracking()
            .Where(unit => unitIds.Contains(unit.Id))
            .ToDictionaryAsync(unit => unit.Id, unit => unit.Abbreviation, cancellationToken);

        var reservedByItem = holds
            .GroupBy(hold => hold.InventoryItemId)
            .ToDictionary(group => group.Key, group => group.Sum(hold => hold.Held));

        var isDraft = supplierReturn.Status == SupplierReturnStatus.Draft;
        var lineItems = new List<SupplierReturnLineItem>(lines.Count);
        var blockers = new List<SupplierReturnPostingBlocker>();

        // The preview token covers everything posting depends on, so a post made against an
        // outdated preview is rejected even when intermediate movements restored the same quantity.
        var fingerprint = new StringBuilder()
            .Append(supplierReturn.Id)
            .Append('|')
            .Append(supplierReturn.ConcurrencyToken)
            .Append('|')
            .Append(purchaseOrderStatus);

        if (isDraft && purchaseOrderStatus != PurchaseOrderStatus.Received)
        {
            blockers.Add(new SupplierReturnPostingBlocker(
                null,
                null,
                SupplierReturnBlockerCodes.PurchaseOrderNotReceived,
                $"Purchase order '{supplierReturn.PurchaseOrderId}' is {purchaseOrderStatus}; only received orders can be returned.",
                []));
        }

        if (isDraft && lines.Count == 0)
        {
            blockers.Add(new SupplierReturnPostingBlocker(
                null,
                null,
                SupplierReturnBlockerCodes.NoLines,
                "The return has no lines to post.",
                []));
        }

        foreach (var line in lines.OrderBy(line => line.PurchaseOrderLineId))
        {
            var item = items[line.InventoryItemId];
            var received = receivedQuantities.GetValueOrDefault(line.PurchaseOrderLineId);
            var previouslyReturned = postedQuantities.GetValueOrDefault(line.PurchaseOrderLineId);
            var returnable = received - previouslyReturned;
            var remaining = supplierReturn.Status == SupplierReturnStatus.Posted
                ? returnable - line.Quantity
                : returnable;

            if (isDraft && line.Quantity > returnable)
            {
                blockers.Add(new SupplierReturnPostingBlocker(
                    line.PurchaseOrderLineId,
                    item.Id,
                    SupplierReturnBlockerCodes.ExceedsReturnable,
                    $"Returning {Format(line.Quantity)} of '{item.PartNumber}' exceeds the remaining returnable "
                        + $"quantity {Format(returnable)} (received {Format(received)}, already returned "
                        + $"{Format(previouslyReturned)}).",
                    []));
            }

            lineItems.Add(new SupplierReturnLineItem(
                line.Id,
                line.PurchaseOrderLineId,
                item.Id,
                item.Name,
                item.PartNumber,
                units.GetValueOrDefault(item.UnitId, string.Empty),
                received,
                previouslyReturned,
                remaining,
                item.Quantity,
                reservedByItem.GetValueOrDefault(item.Id),
                line.Quantity,
                line.Reason));

            fingerprint
                .Append('|').Append(line.Id)
                .Append(':').Append(line.PurchaseOrderLineId)
                .Append(':').Append(Format(line.Quantity))
                .Append(':').Append(Format(received))
                .Append(':').Append(Format(previouslyReturned));
        }

        var previewItems = new List<SupplierReturnPreviewItem>(itemIds.Count);

        // Several purchase-order lines can carry the same part, so stock is checked per item.
        foreach (var group in lines.GroupBy(line => line.InventoryItemId).OrderBy(group => group.Key))
        {
            var item = items[group.Key];
            var returnQuantity = group.Sum(line => line.Quantity);
            var reserved = reservedByItem.GetValueOrDefault(item.Id);
            var available = item.Quantity - reserved;

            if (isDraft && returnQuantity > item.Quantity)
            {
                blockers.Add(new SupplierReturnPostingBlocker(
                    null,
                    item.Id,
                    SupplierReturnBlockerCodes.InsufficientStock,
                    $"Returning {Format(returnQuantity)} of '{item.PartNumber}' exceeds the on-hand quantity "
                        + $"{Format(item.Quantity)}.",
                    []));
            }
            else if (isDraft && returnQuantity > available)
            {
                blockers.Add(new SupplierReturnPostingBlocker(
                    null,
                    item.Id,
                    SupplierReturnBlockerCodes.ReservedStock,
                    $"Returning {Format(returnQuantity)} of '{item.PartNumber}' would consume stock reserved for "
                        + $"maintenance: only {Format(Math.Max(available, 0m))} of {Format(item.Quantity)} on hand is "
                        + $"unreserved ({Format(reserved)} reserved). A planner must reduce or release the reservations "
                        + "on the listed work orders before this return can be posted.",
                    holds
                        .Where(hold => hold.InventoryItemId == item.Id)
                        .Select(hold => hold.WorkOrderId)
                        .Distinct()
                        .Order()
                        .ToList()));
            }

            previewItems.Add(new SupplierReturnPreviewItem(
                item.Id,
                item.Name,
                item.PartNumber,
                item.Quantity,
                reserved,
                available,
                returnQuantity,
                item.Quantity - returnQuantity));

            fingerprint
                .Append('|').Append(item.Id)
                .Append(':').Append(item.StockConcurrencyToken)
                .Append(':').Append(item.ReservationConcurrencyToken)
                .Append(':').Append(Format(item.Quantity))
                .Append(':').Append(Format(reserved));
        }

        var previewToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));

        var orderedLineItems = lineItems
            .OrderBy(line => line.Name, StringComparer.Ordinal)
            .ThenBy(line => line.PartNumber, StringComparer.Ordinal)
            .ThenBy(line => line.PurchaseOrderLineId)
            .ToList();

        return new Evaluation(orderedLineItems, previewItems, blockers, previewToken);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
