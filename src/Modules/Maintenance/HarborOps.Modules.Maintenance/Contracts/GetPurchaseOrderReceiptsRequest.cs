using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPurchaseOrderReceiptsRequest
{
    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
