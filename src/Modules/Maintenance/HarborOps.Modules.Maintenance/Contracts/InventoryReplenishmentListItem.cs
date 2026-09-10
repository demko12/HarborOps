using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record InventoryReplenishmentListItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    Guid SupplierId,
    string SupplierName,
    decimal StockOnHand,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    decimal ReorderLevel,
    decimal ShortageQuantity,
    decimal OpenPurchaseOrderQuantity,
    string Classification);

public static class InventoryReplenishmentClassifications
{
    public const string Critical = "Critical";

    public const string Low = "Low";

    public const string Covered = "Covered";
}
