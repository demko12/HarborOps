using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class PurchaseOrderTransitionException(
    Guid purchaseOrderId,
    PurchaseOrderStatus currentStatus,
    PurchaseOrderStatus targetStatus)
    : Exception($"Purchase order '{purchaseOrderId}' cannot move from status '{currentStatus}' to '{targetStatus}'.")
{
    internal Guid PurchaseOrderId { get; } = purchaseOrderId;

    internal PurchaseOrderStatus CurrentStatus { get; } = currentStatus;

    internal PurchaseOrderStatus TargetStatus { get; } = targetStatus;
}
