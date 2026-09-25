using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnMovementItem(
    Guid MovementId,
    Guid InventoryItemId,
    Guid SupplierReturnId,
    Guid SupplierReturnLineId,
    decimal QuantityBefore,
    decimal QuantityAfter,
    decimal QuantityDelta,
    string Reason,
    Guid ActorUserId,
    DateTime MovedUtc);
