using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record UpdateSupplierReturnRequest
{
    public Guid? ExpectedConcurrencyToken { get; init; }

    public string? Notes { get; init; }

    // Replaces the draft lines: omitted purchase-order lines are removed from the return.
    public IReadOnlyList<SupplierReturnLineEntry>? Lines { get; init; }
}
