using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.IntegrationTests.Maintenance;

internal static class PurchaseOrderSeedData
{
    // Brings a new order and its lines to the requested status the way the application would. Received
    // lines are filled through PurchaseOrderLine.Receive without a receipt row; PartiallyReceived receives
    // half of the first line.
    internal static void ApplyStatus(
        PurchaseOrder order,
        PurchaseOrderStatus status,
        IReadOnlyCollection<PurchaseOrderLine> lines)
    {
        switch (status)
        {
            case PurchaseOrderStatus.Ordered:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                break;
            case PurchaseOrderStatus.PartiallyReceived:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);
                lines.First().Receive(lines.First().QuantityOrdered / 2);
                order.RecordReceipt(lines);
                break;
            case PurchaseOrderStatus.Received:
                order.ChangeStatusTo(PurchaseOrderStatus.Ordered);

                foreach (var line in lines)
                {
                    line.Receive(line.OutstandingQuantity);
                }

                order.RecordReceipt(lines);
                break;
            case PurchaseOrderStatus.Cancelled:
                order.ChangeStatusTo(PurchaseOrderStatus.Cancelled);
                break;
        }
    }
}
