using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnLineItem(
    Guid SupplierReturnLineId,
    Guid PurchaseOrderLineId,
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    string UnitAbbreviation,
    decimal ReceivedQuantity,
    decimal PreviouslyReturnedQuantity,
    decimal RemainingReturnableQuantity,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal Quantity,
    string Reason);
