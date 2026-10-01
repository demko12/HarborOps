using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderClosurePreviewLine(
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal QuantityOrdered,
    decimal ReceivedQuantity,
    decimal ReturnedQuantity,
    // Open outstanding quantity that closing records as closed without delivery.
    decimal QuantityToClose);
