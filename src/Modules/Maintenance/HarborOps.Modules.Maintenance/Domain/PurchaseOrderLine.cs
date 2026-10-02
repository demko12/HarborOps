using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class PurchaseOrderLine
{
    private PurchaseOrderLine()
    {
    }

    private PurchaseOrderLine(
        Guid id,
        Guid purchaseOrderId,
        Guid partId,
        decimal quantityOrdered,
        decimal unitCost)
    {
        Id = id;
        PurchaseOrderId = purchaseOrderId;
        PartId = partId;
        QuantityOrdered = quantityOrdered;
        UnitCost = unitCost;
    }

    public Guid Id { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    public Guid PartId { get; private set; }

    public decimal QuantityOrdered { get; private set; }

    public decimal UnitCost { get; private set; }

    // Cumulative quantity received against this line, including any legacy opening balance.
    public decimal ReceivedQuantity { get; private set; }

    // Quantity recorded as received before receipts were tracked; it has no receipt, date, or actor.
    public decimal LegacyReceivedQuantity { get; private set; }

    // Quantity closed without delivery when the order was closed short; it never reaches stock.
    public decimal ClosedQuantity { get; private set; }

    // Open outstanding: ordered, less what was received, less what was closed without delivery.
    internal decimal OutstandingQuantity => QuantityOrdered - ReceivedQuantity - ClosedQuantity;

    internal bool IsFulfilled => ReceivedQuantity >= QuantityOrdered;

    internal static PurchaseOrderLine Create(
        Guid purchaseOrderId,
        Guid partId,
        decimal quantityOrdered,
        decimal unitCost)
    {
        if (quantityOrdered <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantityOrdered),
                "Ordered quantity must be greater than zero.");
        }

        if (unitCost < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(unitCost),
                "Unit cost must not be negative.");
        }
        return new PurchaseOrderLine(
            Guid.NewGuid(),
            purchaseOrderId,
            partId,
            quantityOrdered,
            unitCost);
    }

    internal void Receive(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Received quantity for purchase order line '{Id}' must be greater than zero.");
        }

        if (quantity > OutstandingQuantity)
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Receiving {quantity} on purchase order line '{Id}' exceeds the outstanding quantity {OutstandingQuantity}.");
        }

        ReceivedQuantity += quantity;
    }

    // Closes the entire open outstanding quantity without delivery; ordered and received stay untouched.
    internal decimal CloseOutstanding()
    {
        if (ClosedQuantity != 0)
        {
            throw new PurchaseOrderClosureInvariantException(
                $"Purchase order line '{Id}' has already been closed.");
        }

        var closedQuantity = Math.Max(OutstandingQuantity, 0m);
        ClosedQuantity = closedQuantity;

        return closedQuantity;
    }

    internal decimal LineTotal()
    {
        return QuantityOrdered * UnitCost;
    }

    internal static decimal OrderTotal(IEnumerable<PurchaseOrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        return lines.Sum(line => line.LineTotal());
    }
}
