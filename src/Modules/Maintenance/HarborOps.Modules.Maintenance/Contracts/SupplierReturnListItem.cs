using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnListItem(
    Guid SupplierReturnId,
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierName,
    string Status,
    int LineCount,
    decimal TotalQuantity,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    DateTime? PostedUtc,
    DateTime? CancelledUtc,
    Guid ConcurrencyToken);
