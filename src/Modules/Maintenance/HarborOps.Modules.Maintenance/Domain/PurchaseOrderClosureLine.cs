using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

// Snapshot of the quantities a closure was based on; later returns never change it.
internal sealed class PurchaseOrderClosureLine
{
    private PurchaseOrderClosureLine()
    {
    }

    private PurchaseOrderClosureLine(
        Guid id,
        Guid purchaseOrderClosureId,
        Guid purchaseOrderLineId,
        Guid inventoryItemId,
        decimal quantityOrdered,
        decimal receivedQuantity,
        decimal returnedQuantity,
        decimal closedQuantity)
    {
        Id = id;
        PurchaseOrderClosureId = purchaseOrderClosureId;
        PurchaseOrderLineId = purchaseOrderLineId;
        InventoryItemId = inventoryItemId;
        QuantityOrdered = quantityOrdered;
        ReceivedQuantity = receivedQuantity;
        ReturnedQuantity = returnedQuantity;
        ClosedQuantity = closedQuantity;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderClosureId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public decimal QuantityOrdered { get; private set; }

    // Gross received quantity of the line at closing, including any legacy opening balance.
    public decimal ReceivedQuantity { get; private set; }

    // Posted supplier returns of the line at closing; informational only.
    public decimal ReturnedQuantity { get; private set; }

    // Quantity closed without delivery; zero for a line that was already fully received.
    public decimal ClosedQuantity { get; private set; }

    // Called after the purchase-order line has closed its outstanding quantity.
    internal static PurchaseOrderClosureLine Create(
        PurchaseOrderClosure closure,
        PurchaseOrderLine purchaseOrderLine,
        decimal returnedQuantity)
    {
        if (purchaseOrderLine.PurchaseOrderId != closure.PurchaseOrderId)
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Purchase order line '{purchaseOrderLine.Id}' does not belong to purchase order "
                    + $"'{closure.PurchaseOrderId}'.");
        }

        if (returnedQuantity < 0 || returnedQuantity > purchaseOrderLine.ReceivedQuantity)
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Returned quantity for purchase order line '{purchaseOrderLine.Id}' must be between zero and the "
                    + "received quantity.");
        }

        return new PurchaseOrderClosureLine(
            Guid.NewGuid(),
            closure.Id,
            purchaseOrderLine.Id,
            purchaseOrderLine.PartId,
            purchaseOrderLine.QuantityOrdered,
            purchaseOrderLine.ReceivedQuantity,
            returnedQuantity,
            purchaseOrderLine.ClosedQuantity);
    }
}
