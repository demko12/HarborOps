using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPurchaseOrderReceiptsResponse(
    IReadOnlyList<PurchaseOrderReceiptDetails> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
