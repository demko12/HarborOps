using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptLineItem(
    Guid ReceiptLineId,
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal Quantity,
    decimal LineReceivedQuantityAfter,
    decimal OutstandingQuantityAfter);
