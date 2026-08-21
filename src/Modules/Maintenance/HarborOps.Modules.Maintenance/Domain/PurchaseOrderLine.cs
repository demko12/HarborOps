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
