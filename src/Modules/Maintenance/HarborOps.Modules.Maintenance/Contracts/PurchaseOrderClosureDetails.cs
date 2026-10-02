using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderClosureDetails(
    Guid ClosureId,
    Guid PurchaseOrderId,
    Guid ClientRequestId,
    string Reason,
    Guid ClosedByUserId,
    DateTime ClosedUtc,
    string OrderStatusBefore,
    string OrderStatusAfter,
    decimal TotalQuantityReceived,
    decimal TotalQuantityClosed,
    IReadOnlyList<PurchaseOrderClosureLineItem> Lines);
