using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetPurchaseOrderDetailsResponse(
    Guid PurchaseOrderId,
    Guid SupplierId,
    string SupplierName,
    string? SupplierContactPerson,
    string? SupplierPhoneEmail,
    DateOnly OrderDate,
    DateOnly? ExpectedDelivery,
    string Status,
    bool IsOverdue,
    int? DaysUntilDelivery,
    decimal TotalCost,
    IReadOnlyList<string> AllowedTransitions,
    IReadOnlyList<PurchaseOrderLineItem> Lines,
    Guid ConcurrencyToken,
    decimal TotalQuantityOrdered,
    decimal TotalQuantityReceived,
    decimal TotalQuantityReturned,
    decimal TotalQuantityOutstanding,
    int ReceiptCount,
    IReadOnlyList<string> AllowedActions);
