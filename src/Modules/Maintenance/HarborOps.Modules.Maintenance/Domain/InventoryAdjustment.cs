using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class InventoryAdjustment
{
    private InventoryAdjustment()
    {
    }

    private InventoryAdjustment(
        Guid id,
        Guid inventoryItemId,
        Guid? stocktakeId,
        Guid? supplierReturnId,
        Guid? supplierReturnLineId,
        Guid? purchaseOrderReceiptId,
        Guid? purchaseOrderReceiptLineId,
        decimal quantityBefore,
        decimal quantityAfter,
        string reason,
        Guid actorUserId,
        DateTime adjustedUtc)
    {
        Id = id;
        InventoryItemId = inventoryItemId;
        StocktakeId = stocktakeId;
        SupplierReturnId = supplierReturnId;
        SupplierReturnLineId = supplierReturnLineId;
        PurchaseOrderReceiptId = purchaseOrderReceiptId;
        PurchaseOrderReceiptLineId = purchaseOrderReceiptLineId;
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityAfter;
        QuantityDelta = quantityAfter - quantityBefore;
        Reason = reason;
        ActorUserId = actorUserId;
        AdjustedUtc = adjustedUtc;
    }

    public Guid Id { get; private set; }

    public Guid InventoryItemId { get; private set; }

    // Exactly one source is set: the stocktake, the supplier return (with its line), or the purchase-order
    // receipt (with its line) that moved the stock.
    public Guid? StocktakeId { get; private set; }

    public Guid? SupplierReturnId { get; private set; }

    public Guid? SupplierReturnLineId { get; private set; }

    public Guid? PurchaseOrderReceiptId { get; private set; }

    public Guid? PurchaseOrderReceiptLineId { get; private set; }

    public decimal QuantityBefore { get; private set; }

    public decimal QuantityAfter { get; private set; }

    public decimal QuantityDelta { get; private set; }

    public string Reason { get; private set; } = null!;

    public Guid ActorUserId { get; private set; }

    public DateTime AdjustedUtc { get; private set; }

    internal static InventoryAdjustment CreateFromStocktake(
        Guid inventoryItemId,
        Guid stocktakeId,
        decimal quantityBefore,
        decimal countedQuantity,
        string reason,
        Guid actorUserId,
        DateTime adjustedUtc)
    {
        return new InventoryAdjustment(
            Guid.NewGuid(),
            inventoryItemId,
            stocktakeId,
            null,
            null,
            null,
            null,
            quantityBefore,
            countedQuantity,
            reason,
            actorUserId,
            adjustedUtc);
    }

    internal static InventoryAdjustment CreateFromSupplierReturn(
        SupplierReturnLine line,
        decimal quantityBefore,
        Guid actorUserId,
        DateTime adjustedUtc)
    {
        if (line.Quantity > quantityBefore)
        {
            throw new SupplierReturnInvariantException(
                $"Returning {line.Quantity} of item '{line.InventoryItemId}' would make stock negative.");
        }

        return new InventoryAdjustment(
            Guid.NewGuid(),
            line.InventoryItemId,
            null,
            line.SupplierReturnId,
            line.Id,
            null,
            null,
            quantityBefore,
            quantityBefore - line.Quantity,
            line.Reason,
            actorUserId,
            adjustedUtc);
    }

    internal static InventoryAdjustment CreateFromPurchaseOrderReceipt(
        PurchaseOrderReceipt receipt,
        PurchaseOrderReceiptLine line,
        decimal quantityBefore,
        Guid actorUserId,
        DateTime adjustedUtc)
    {
        if (line.PurchaseOrderReceiptId != receipt.Id)
        {
            throw new PurchaseOrderReceiptInvariantException(
                $"Receipt line '{line.Id}' does not belong to receipt '{receipt.Id}'.");
        }

        return new InventoryAdjustment(
            Guid.NewGuid(),
            line.InventoryItemId,
            null,
            null,
            null,
            receipt.Id,
            line.Id,
            quantityBefore,
            quantityBefore + line.Quantity,
            receipt.MovementReason(),
            actorUserId,
            adjustedUtc);
    }
}
