using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPurchaseOrdersResponse(
    IReadOnlyList<PurchaseOrderListItem> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
