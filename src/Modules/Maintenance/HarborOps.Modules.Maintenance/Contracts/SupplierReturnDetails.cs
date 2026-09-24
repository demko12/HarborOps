using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record SupplierReturnDetails(
    Guid SupplierReturnId,
    Guid PurchaseOrderId,
    string PurchaseOrderStatus,
    Guid SupplierId,
    string SupplierName,
    string Status,
    string? Notes,
    Guid ConcurrencyToken,
    Guid CreatedByUserId,
    DateTime CreatedUtc,
    Guid? LastModifiedByUserId,
    DateTime? LastModifiedUtc,
    Guid? PostedByUserId,
    DateTime? PostedUtc,
    Guid? CancelledByUserId,
    DateTime? CancelledUtc,
    string? CancellationReason,
    IReadOnlyList<SupplierReturnLineItem> Lines,
    IReadOnlyList<SupplierReturnPostingBlocker> Blockers,
    IReadOnlyList<SupplierReturnMovementItem> Movements);
