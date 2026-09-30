using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class PurchaseOrderReceiptLine
{
    internal const int QuantityScale = 3;
    internal const decimal MaxQuantity = 999_999_999_999_999.999m;

    private PurchaseOrderReceiptLine()
    {
    }

    private PurchaseOrderReceiptLine(
        Guid id,
        Guid purchaseOrderReceiptId,
        Guid purchaseOrderLineId,
        Guid inventoryItemId,
        decimal quantity,
        decimal lineReceivedQuantityAfter)
    {
        Id = id;
        PurchaseOrderReceiptId = purchaseOrderReceiptId;
        PurchaseOrderLineId = purchaseOrderLineId;
        InventoryItemId = inventoryItemId;
        Quantity = quantity;
        LineReceivedQuantityAfter = lineReceivedQuantityAfter;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderReceiptId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public decimal Quantity { get; private set; }

    // Cumulative received quantity of the purchase-order line right after this receipt.
    public decimal LineReceivedQuantityAfter { get; private set; }

    // Called after the purchase-order line has taken the quantity, so its cumulative total is final.
    internal static PurchaseOrderReceiptLine Create(
        PurchaseOrderReceipt receipt,
        PurchaseOrderLine purchaseOrderLine,
        decimal quantity)
    {
        if (purchaseOrderLine.PurchaseOrderId != receipt.PurchaseOrderId)
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Purchase order line '{purchaseOrderLine.Id}' does not belong to purchase order "
                    + $"'{receipt.PurchaseOrderId}'.");
        }

        if (quantity <= 0 || quantity > MaxQuantity || !HasSupportedPrecision(quantity))
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Received quantity for purchase order line '{purchaseOrderLine.Id}' must be greater than zero "
                    + $"with at most {QuantityScale} decimal places.");
        }

        return new PurchaseOrderReceiptLine(
            Guid.NewGuid(),
            receipt.Id,
            purchaseOrderLine.Id,
            purchaseOrderLine.PartId,
            quantity,
            purchaseOrderLine.ReceivedQuantity);
    }

    internal static bool HasSupportedPrecision(decimal quantity)
    {
        return decimal.Round(quantity, QuantityScale) == quantity;
    }
}
