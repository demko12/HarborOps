using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PostPurchaseOrderReceiptRequest
{
    // Chosen by the client; an identical retry returns the original receipt instead of receiving twice.
    public Guid? ClientRequestId { get; init; }

    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }

    public DateTime? ReceivedUtc { get; init; }

    public string? DeliveryReference { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<PurchaseOrderReceiptLineEntry>? Lines { get; init; }
}
