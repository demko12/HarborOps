using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HarborOps.Modules.Maintenance.Contracts;
using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.Modules.Maintenance.Application;

// Shared by the preview and the close, so a confirmed preview is checked against exactly the same rules.
internal static class PurchaseOrderClosureEvaluator
{
    internal sealed record Evaluation(
        IReadOnlyList<PurchaseOrderClosurePreviewLine> Lines,
        IReadOnlyList<PurchaseOrderClosureBlocker> Blockers,
        IReadOnlyDictionary<Guid, decimal> ReturnedQuantities,
        PurchaseOrderStatus ResultingStatus,
        string PreviewToken);

    internal static async Task<Evaluation> EvaluateAsync(
        DbContext dbContext,
        PurchaseOrder purchaseOrder,
        IReadOnlyCollection<PurchaseOrderLine> orderLines,
        CancellationToken cancellationToken)
    {
        var itemIds = orderLines.Select(line => line.PartId).Distinct().ToList();

        // Closing moves no stock, so items are read only to describe the lines.
        var items = await dbContext.Set<InventoryItem>()
            .AsNoTracking()
            .Where(item => itemIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var units = await PurchaseOrderReceiptReader.ReadUnitsAsync(dbContext, items.Values, cancellationToken);

        var returnedQuantities = await SupplierReturnReader.ReadPostedQuantitiesAsync(
            dbContext,
            orderLines.Select(line => line.Id).ToList(),
            null,
            cancellationToken);

        var hasReceipts = await dbContext.Set<PurchaseOrderReceipt>()
            .AsNoTracking()
            .AnyAsync(receipt => receipt.PurchaseOrderId == purchaseOrder.Id, cancellationToken);

        var blockers = new List<PurchaseOrderClosureBlocker>();

        // The preview token covers every quantity the closure records, so closing against an outdated
        // preview is rejected even when the order token happens to be current.
        var fingerprint = new StringBuilder()
            .Append(purchaseOrder.Id)
            .Append('|')
            .Append(purchaseOrder.ConcurrencyToken)
            .Append('|')
            .Append(purchaseOrder.Status)
            .Append('|')
            .Append(hasReceipts);

        if (!PurchaseOrder.CanClose(purchaseOrder.Status))
        {
            blockers.Add(new PurchaseOrderClosureBlocker(
                null,
                PurchaseOrderClosureBlockerCodes.OrderNotClosable,
                purchaseOrder.Status is PurchaseOrderStatus.Draft or PurchaseOrderStatus.Ordered
                    ? $"Purchase order '{purchaseOrder.Id}' is {purchaseOrder.Status} with nothing received; cancel it "
                        + "instead of closing it short."
                    : $"Purchase order '{purchaseOrder.Id}' is {purchaseOrder.Status}; only partially received orders "
                        + "can be closed short."));
        }
        else if (!hasReceipts)
        {
            blockers.Add(new PurchaseOrderClosureBlocker(
                null,
                PurchaseOrderClosureBlockerCodes.NoReceipts,
                $"Purchase order '{purchaseOrder.Id}' has no recorded receipt; only orders with a posted receipt can "
                    + "be closed short."));
        }
        else if (orderLines.All(line => line.OutstandingQuantity <= 0))
        {
            blockers.Add(new PurchaseOrderClosureBlocker(
                null,
                PurchaseOrderClosureBlockerCodes.NothingOutstanding,
                $"Nothing remains outstanding on purchase order '{purchaseOrder.Id}'."));
        }

        var previewLines = new List<PurchaseOrderClosurePreviewLine>(orderLines.Count);

        foreach (var line in orderLines.OrderBy(line => line.Id))
        {
            var item = items.GetValueOrDefault(line.PartId);
            var returned = returnedQuantities.GetValueOrDefault(line.Id);

            previewLines.Add(new PurchaseOrderClosurePreviewLine(
                line.Id,
                line.PartId,
                item?.Name ?? string.Empty,
                item?.PartNumber ?? string.Empty,
                item is null ? string.Empty : units.GetValueOrDefault(item.UnitId, string.Empty),
                line.QuantityOrdered,
                line.ReceivedQuantity,
                returned,
                Math.Max(line.OutstandingQuantity, 0m)));

            fingerprint
                .Append('|').Append(line.Id)
                .Append(':').Append(Format(line.QuantityOrdered))
                .Append(':').Append(Format(line.ReceivedQuantity))
                .Append(':').Append(Format(line.ClosedQuantity))
                .Append(':').Append(Format(returned));
        }

        var resultingStatus = blockers.Count > 0 ? purchaseOrder.Status : PurchaseOrderStatus.ClosedShort;

        var previewToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));

        var orderedLines = previewLines
            .OrderBy(line => line.Name, StringComparer.Ordinal)
            .ThenBy(line => line.PartNumber, StringComparer.Ordinal)
            .ThenBy(line => line.PurchaseOrderLineId)
            .ToList();

        return new Evaluation(orderedLines, blockers, returnedQuantities, resultingStatus, previewToken);
    }

    internal static PurchaseOrderClosurePreview ToPreview(
        PurchaseOrder purchaseOrder,
        Evaluation evaluation,
        DateTime generatedUtc)
    {
        return new PurchaseOrderClosurePreview(
            purchaseOrder.Id,
            purchaseOrder.Status.ToString(),
            purchaseOrder.ConcurrencyToken,
            evaluation.PreviewToken,
            evaluation.Blockers.Count == 0,
            evaluation.ResultingStatus.ToString(),
            evaluation.Lines.Sum(line => line.ReceivedQuantity),
            evaluation.Lines.Sum(line => line.QuantityToClose),
            evaluation.Lines,
            evaluation.Blockers,
            generatedUtc);
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
