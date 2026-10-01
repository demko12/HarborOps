using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ClosePurchaseOrderRequest
{
    // Chosen by the client; an identical retry returns the original closure instead of failing.
    public Guid? ClientRequestId { get; init; }

    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? PreviewToken { get; init; }

    public string? Reason { get; init; }
}
