using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderLineItem(
    Guid LineId,
    Guid InventoryItemId,
    string InventoryItemName,
    string PartNumber,
    decimal QuantityOrdered,
    decimal UnitCost,
    decimal LineTotal,
    decimal ReceivedQuantity,
    // Part of ReceivedQuantity recorded before receipts were tracked; it has no receipt behind it.
    decimal LegacyReceivedQuantity,
    decimal ReturnedQuantity,
    // Undelivered quantity closed when the order was closed short; it is not outstanding.
    decimal ClosedQuantity,
    decimal OutstandingQuantity,
    decimal ReturnableQuantity);
