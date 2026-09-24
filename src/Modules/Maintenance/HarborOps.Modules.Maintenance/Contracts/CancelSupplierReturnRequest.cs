using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CancelSupplierReturnRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Reason { get; init; }
}
