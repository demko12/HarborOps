using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class SupplierReturnLine
{
    internal const int QuantityScale = 3;
    internal const decimal MaxQuantity = 999_999_999_999_999.999m;

    private SupplierReturnLine()
    {
    }

    private SupplierReturnLine(
        Guid id,
        Guid supplierReturnId,
        Guid purchaseOrderLineId,
        Guid inventoryItemId)
    {
        Id = id;
        SupplierReturnId = supplierReturnId;
        PurchaseOrderLineId = purchaseOrderLineId;
        InventoryItemId = inventoryItemId;
    }

    public Guid Id { get; private set; }

    public Guid SupplierReturnId { get; private set; }

    public Guid PurchaseOrderLineId { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public decimal Quantity { get; private set; }

    public string Reason { get; private set; } = null!;

    internal static SupplierReturnLine Create(
        SupplierReturn supplierReturn,
        PurchaseOrderLine purchaseOrderLine,
        decimal quantity,
        string reason)
    {
        supplierReturn.EnsureDraft();

        if (purchaseOrderLine.PurchaseOrderId != supplierReturn.PurchaseOrderId)
        {
            throw new SupplierReturnInvariantException(
                $"Purchase order line '{purchaseOrderLine.Id}' does not belong to purchase order "
                    + $"'{supplierReturn.PurchaseOrderId}'.");
        }

        var line = new SupplierReturnLine(
            Guid.NewGuid(),
            supplierReturn.Id,
            purchaseOrderLine.Id,
            purchaseOrderLine.PartId);

        line.Change(quantity, reason);

        return line;
    }

    internal static bool HasSupportedPrecision(decimal quantity)
    {
        return decimal.Round(quantity, QuantityScale) == quantity;
    }

    internal void Change(decimal quantity, string reason)
    {
        if (quantity <= 0 || quantity > MaxQuantity)
        {
            throw new SupplierReturnInvariantException(
                $"Return quantity for purchase order line '{PurchaseOrderLineId}' must be greater than zero.");
        }

        if (!HasSupportedPrecision(quantity))
        {
            throw new SupplierReturnInvariantException(
                $"Return quantity for purchase order line '{PurchaseOrderLineId}' supports at most "
                    + $"{QuantityScale} decimal places.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new SupplierReturnInvariantException(
                $"A return reason is required for purchase order line '{PurchaseOrderLineId}'.");
        }

        var normalizedReason = reason.Trim();

        if (normalizedReason.Length > SupplierReturn.ReasonMaxLength)
        {
            throw new SupplierReturnInvariantException(
                $"Return reason must not exceed {SupplierReturn.ReasonMaxLength} characters.");
        }

        Quantity = quantity;
        Reason = normalizedReason;
    }
}
