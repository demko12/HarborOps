using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderClosureLineItem(
    Guid ClosureLineId,
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal QuantityOrdered,
    decimal ReceivedQuantity,
    decimal ReturnedQuantity,
    decimal ClosedQuantity);
