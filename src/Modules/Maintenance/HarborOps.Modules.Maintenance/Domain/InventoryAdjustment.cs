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
        Guid stocktakeId,
        decimal quantityBefore,
        decimal countedQuantity,
        decimal quantityDelta,
        string reason,
        Guid actorUserId,
        DateTime adjustedUtc)
    {
        Id = id;
        InventoryItemId = inventoryItemId;
        StocktakeId = stocktakeId;
        QuantityBefore = quantityBefore;
        CountedQuantity = countedQuantity;
        QuantityDelta = quantityDelta;
        Reason = reason;
        ActorUserId = actorUserId;
        AdjustedUtc = adjustedUtc;
    }

    public Guid Id { get; private set; }

    public Guid InventoryItemId { get; private set; }

    public Guid StocktakeId { get; private set; }

    public decimal QuantityBefore { get; private set; }

    public decimal CountedQuantity { get; private set; }

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
            quantityBefore,
            countedQuantity,
            countedQuantity - quantityBefore,
            reason,
            actorUserId,
            adjustedUtc);
    }
}
