using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetInventoryItemsRequest
{
    public string? Search { get; init; }

    public bool? LowStock { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
