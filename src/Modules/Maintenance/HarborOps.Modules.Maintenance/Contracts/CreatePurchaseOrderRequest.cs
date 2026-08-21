using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record CreatePurchaseOrderRequest
{
    public Guid? SupplierId { get; init; }

    public DateOnly? OrderDate { get; init; }

    public DateOnly? ExpectedDelivery { get; init; }

    public IReadOnlyList<CreatePurchaseOrderLineRequest>? Lines { get; init; }
}
