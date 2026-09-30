using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PurchaseOrderListItem(
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierName,
    DateOnly OrderDate,
    DateOnly? ExpectedDelivery,
    string Status,
    int LineCount,
    decimal TotalCost,
    bool IsOverdue,
    int? DaysUntilDelivery,
    decimal QuantityOrdered,
    decimal QuantityReceived,
    decimal QuantityOutstanding);
