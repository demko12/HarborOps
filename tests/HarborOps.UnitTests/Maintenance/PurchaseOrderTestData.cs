using HarborOps.Modules.Maintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace HarborOps.UnitTests.Maintenance;

internal static class PurchaseOrderTestData
{
    // Brings a new order and its lines to the requested status the way the application would. Received
    // lines are filled through PurchaseOrderLine.Receive, like a legacy opening balance without a receipt row.
    // PartiallyReceived receives half of the first line.
    internal static void ApplyStatus(
        PurchaseOrder order,
        PurchaseOrderStatus status,
        IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        switch (status)
        {
            case PurchaseOrderStatus.Draft:
                break;
            case PurchaseOrderStatus.Ordered:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                break;
            case PurchaseOrderStatus.PartiallyReceived:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                ReceivePartially(order, lines, new Dictionary<Guid, decimal>
                {
                    [lines.First().Id] = lines.First().QuantityOrdered / 2,
                });
                break;
            case PurchaseOrderStatus.Received:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                ReceiveInFull(order, lines);
                break;
            case PurchaseOrderStatus.Cancelled:
                order.ChangeStatusTo(PurchaseOrderStatus.Cancelled);
                break;
            case PurchaseOrderStatus.ClosedShort:
                ApplyStatus(order, PurchaseOrderStatus.PartiallyReceived, lines);
                CloseShort(order, lines);
                break;
        }
    }

    // Closes the open outstanding quantity of every line without a closure row, the way the domain does it.
    internal static void CloseShort(PurchaseOrder order, IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        foreach (var line in lines)
        {
            line.CloseOutstanding();
        }

        order.RecordClosure(lines);
    }

    // Mirrors the AddMaintenancePurchaseOrderReceipts migration for an order received before receipts were
    // tracked: every line gets a legacy opening balance, no receipt row exists, and stock is left alone.
    internal static async Task SeedLegacyReceivedAsync(
        DbContext dbContext,
        PurchaseOrder order,
        IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        dbContext.Entry(order).Property(candidate => candidate.Status).CurrentValue = PurchaseOrderStatus.Received;

        foreach (var line in lines)
        {
            var entry = dbContext.Entry(line);
            entry.Property(candidate => candidate.LegacyReceivedQuantity).CurrentValue = line.QuantityOrdered;
            entry.Property(candidate => candidate.ReceivedQuantity).CurrentValue = line.QuantityOrdered;
        }

        await dbContext.SaveChangesAsync();
    }

    internal static void ReceiveInFull(PurchaseOrder order, IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        ReceivePartially(
            order,
            lines,
            lines.Where(line => line.OutstandingQuantity > 0).ToDictionary(line => line.Id, line => line.OutstandingQuantity));
    }

    internal static void ReceivePartially(
        PurchaseOrder order,
        IReadOnlyCollection<PurchaseOrderLine> lines,
        IReadOnlyDictionary<Guid, decimal> quantities)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A received order needs at least one line in test data.");
        }

        foreach (var line in lines.Where(line => quantities.ContainsKey(line.Id)))
        {
            line.Receive(quantities[line.Id]);
        }

        order.RecordReceipt(lines);
    }
}
