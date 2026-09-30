using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PreviewPurchaseOrderReceiptRequest
{
    public DateTime? ReceivedUtc { get; init; }

    public IReadOnlyList<PurchaseOrderReceiptLineEntry>? Lines { get; init; }
}
