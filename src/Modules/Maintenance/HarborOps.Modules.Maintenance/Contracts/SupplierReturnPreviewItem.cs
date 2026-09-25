using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

// One row per inventory item: return lines for the same part are aggregated before stock is checked.
public sealed record SupplierReturnPreviewItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal OnHandQuantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal ReturnQuantity,
    decimal QuantityAfter);
