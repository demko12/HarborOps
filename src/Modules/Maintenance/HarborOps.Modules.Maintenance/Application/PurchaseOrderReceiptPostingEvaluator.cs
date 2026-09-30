using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

// Shared by the preview and the post, so a confirmed preview is checked against exactly the same rules.
internal static class PurchaseOrderReceiptPostingEvaluator
{
    internal sealed record Evaluation(
        IReadOnlyList<PurchaseOrderReceiptPreviewLine> Lines,
        IReadOnlyList<PurchaseOrderReceiptPostingBlocker> Blockers,
        PurchaseOrderStatus ResultingStatus,
        string PreviewToken);

    // The quantities still outstanding per line, which is what receive-remaining posts.
    internal static IReadOnlyDictionary<Guid, decimal> Outstanding(IEnumerable<PurchaseOrderLine> orderLines)
    {
        return orderLines
            .Where(line => line.OutstandingQuantity > 0)
            .ToDictionary(line => line.Id, line => line.OutstandingQuantity);
    }

    internal static async Task<Evaluation> EvaluateAsync(
        DbContext dbContext,
        PurchaseOrder purchaseOrder,
        IReadOnlyCollection<PurchaseOrderLine> orderLines,
        IReadOnlyDictionary<Guid, decimal> requestedQuantities,
        IReadOnlyDictionary<Guid, InventoryItem> items,
        CancellationToken cancellationToken)
    {
        var units = await PurchaseOrderReceiptReader.ReadUnitsAsync(dbContext, items.Values, cancellationToken);

        var blockers = new List<PurchaseOrderReceiptPostingBlocker>();

        // The preview token covers everything posting depends on, so a post made against an outdated
        // preview is rejected even when intermediate movements restored the same quantity.
        var fingerprint = new StringBuilder()
            .Append(purchaseOrder.Id)
            .Append('|')
            .Append(purchaseOrder.ConcurrencyToken)
            .Append('|')
            .Append(purchaseOrder.Status);

        if (!PurchaseOrder.CanReceive(purchaseOrder.Status))
        {
            blockers.Add(new PurchaseOrderReceiptPostingBlocker(
                null,
                PurchaseOrderReceiptBlockerCodes.OrderNotReceivable,
                $"Purchase order '{purchaseOrder.Id}' is {purchaseOrder.Status}; only ordered or partially received "
                    + "orders can be received."));
        }
        else if (requestedQuantities.Count == 0)
        {
            blockers.Add(new PurchaseOrderReceiptPostingBlocker(
                null,
                PurchaseOrderReceiptBlockerCodes.NothingOutstanding,
                $"Nothing remains outstanding on purchase order '{purchaseOrder.Id}'."));
        }

        var receivedLines = orderLines
            .Where(line => requestedQuantities.ContainsKey(line.Id))
            .OrderBy(line => line.Id)
            .ToList();

        var receivedByItem = receivedLines
            .GroupBy(line => line.PartId)
            .ToDictionary(group => group.Key, group => group.Sum(line => requestedQuantities[line.Id]));

        var previewLines = new List<PurchaseOrderReceiptPreviewLine>(receivedLines.Count);

        foreach (var line in receivedLines)
        {
            var quantity = requestedQuantities[line.Id];
            var item = items[line.PartId];

            if (quantity > line.OutstandingQuantity)
            {
                blockers.Add(new PurchaseOrderReceiptPostingBlocker(
                    line.Id,
                    PurchaseOrderReceiptBlockerCodes.ExceedsOutstanding,
                    $"Receiving {Format(quantity)} of '{item.PartNumber}' exceeds the outstanding quantity "
                        + $"{Format(line.OutstandingQuantity)} (ordered {Format(line.QuantityOrdered)}, already received "
                        + $"{Format(line.ReceivedQuantity)})."));
            }

            previewLines.Add(new PurchaseOrderReceiptPreviewLine(
                line.Id,
                item.Id,
                item.Name,
                item.PartNumber,
                units.GetValueOrDefault(item.UnitId, string.Empty),
                line.QuantityOrdered,
                line.ReceivedQuantity,
                line.OutstandingQuantity,
                quantity,
                line.OutstandingQuantity - quantity,
                item.Quantity,
                item.Quantity + receivedByItem[item.Id]));

            fingerprint
                .Append('|').Append(line.Id)
                .Append(':').Append(Format(quantity))
                .Append(':').Append(Format(line.QuantityOrdered))
                .Append(':').Append(Format(line.ReceivedQuantity));
        }

        foreach (var item in receivedByItem.Keys.Order().Select(itemId => items[itemId]))
        {
            fingerprint
                .Append('|').Append(item.Id)
                .Append(':').Append(item.StockConcurrencyToken)
                .Append(':').Append(Format(item.Quantity));
        }

        var resultingStatus = blockers.Count > 0
            ? purchaseOrder.Status
            : orderLines.All(line => line.ReceivedQuantity + requestedQuantities.GetValueOrDefault(line.Id) >= line.QuantityOrdered)
                ? PurchaseOrderStatus.Received
                : PurchaseOrderStatus.PartiallyReceived;

        var previewToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));

        var orderedLines = previewLines
            .OrderBy(line => line.Name, StringComparer.Ordinal)
            .ThenBy(line => line.PartNumber, StringComparer.Ordinal)
            .ThenBy(line => line.PurchaseOrderLineId)
            .ToList();

        return new Evaluation(orderedLines, blockers, resultingStatus, previewToken);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
