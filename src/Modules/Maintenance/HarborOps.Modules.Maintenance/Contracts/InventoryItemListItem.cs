using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record InventoryItemListItem(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal Quantity,
    decimal MinimumQuantity,
    decimal UnitCost,
    Guid CategoryId,
    Guid UnitId,
    Guid SupplierId);
