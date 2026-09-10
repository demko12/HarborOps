using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetInventoryReplenishmentResponse(
    IReadOnlyList<InventoryReplenishmentListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
