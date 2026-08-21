using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPurchaseOrdersRequest
{
    public Guid? SupplierId { get; init; }

    public string? Status { get; init; }

    public DateOnly? OrderDateFrom { get; init; }

    public DateOnly? OrderDateTo { get; init; }

    public DateOnly? ExpectedDeliveryFrom { get; init; }

    public DateOnly? ExpectedDeliveryTo { get; init; }

    public bool? Overdue { get; init; }

    public int? PageNumber { get; init; }

    public int? PageSize { get; init; }
}
