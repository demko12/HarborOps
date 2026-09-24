using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PostStocktakeResponse(
    Guid StocktakeId,
    string Status,
    Guid ConcurrencyToken,
    Guid PostedByUserId,
    DateTime PostedUtc,
    IReadOnlyList<InventoryAdjustmentItem> Adjustments);
