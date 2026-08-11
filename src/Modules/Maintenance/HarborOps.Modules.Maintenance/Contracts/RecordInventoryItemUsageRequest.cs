using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record RecordInventoryItemUsageRequest
{
    public decimal? QuantityUsed { get; init; }
}
