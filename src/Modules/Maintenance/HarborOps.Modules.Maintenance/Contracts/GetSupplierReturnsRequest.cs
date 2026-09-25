using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetSupplierReturnsRequest
{
    public string? Status { get; init; }

    public Guid? PurchaseOrderId { get; init; }

    public Guid? SupplierId { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
