using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptPreviewLine(
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal QuantityOrdered,
    decimal ReceivedQuantity,
    decimal OutstandingQuantity,
    decimal QuantityToReceive,
    decimal OutstandingQuantityAfter,
    decimal OnHandQuantity,
    decimal OnHandQuantityAfter);
