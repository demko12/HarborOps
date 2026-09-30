using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderReceiptLineEntry
{
    public Guid? PurchaseOrderLineId { get; init; }

    public decimal? Quantity { get; init; }
}
