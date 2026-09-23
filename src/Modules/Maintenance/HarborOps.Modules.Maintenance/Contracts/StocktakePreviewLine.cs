using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record StocktakePreviewLine(
    Guid InventoryItemId,
    string Name,
    string PartNumber,
    decimal BaselineQuantity,
    decimal CurrentQuantity,
    decimal? CountedQuantity,
    decimal? Variance,
    decimal OutstandingReservedQuantity,
    bool BaselineIsStale);
