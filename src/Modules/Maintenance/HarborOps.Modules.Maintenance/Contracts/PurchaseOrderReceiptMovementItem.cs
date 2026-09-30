using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptMovementItem(
    Guid MovementId,
    Guid InventoryItemId,
    Guid PurchaseOrderReceiptId,
    Guid PurchaseOrderReceiptLineId,
    decimal QuantityBefore,
    decimal QuantityAfter,
    decimal QuantityDelta,
    string Reason,
    Guid ActorUserId,
    DateTime MovedUtc);
