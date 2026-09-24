using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnLineEntry
{
    public Guid? PurchaseOrderLineId { get; init; }

    public decimal? Quantity { get; init; }

    public string? Reason { get; init; }
}
