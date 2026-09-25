using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record InventoryAdjustmentItem(
    Guid AdjustmentId,
    Guid InventoryItemId,
    Guid StocktakeId,
    decimal QuantityBefore,
    decimal CountedQuantity,
    decimal QuantityDelta,
    string Reason,
    Guid ActorUserId,
    DateTime AdjustedUtc);
