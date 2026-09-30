using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

// Every field is optional so the original body-less receive call keeps working; without a client
// request id a retry is still safe because nothing remains outstanding to receive.
public sealed record ReceiveRemainingPurchaseOrderRequest
{
    public Guid? ClientRequestId { get; init; }

    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }

    public DateTime? ReceivedUtc { get; init; }

    public string? DeliveryReference { get; init; }

    public string? Notes { get; init; }
}
