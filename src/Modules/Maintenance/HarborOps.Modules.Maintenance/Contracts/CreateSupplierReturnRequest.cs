using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CreateSupplierReturnRequest
{
    public Guid? PurchaseOrderId { get; init; }

    public string? Notes { get; init; }

    public IReadOnlyList<SupplierReturnLineEntry>? Lines { get; init; }
}
