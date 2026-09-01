using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class WorkOrderTransitionNotAllowedException(
    Guid workOrderId,
    WorkOrderStatus currentStatus,
    WorkOrderStatus requestedStatus)
    : Exception(
        $"Work order '{workOrderId}' cannot move from status '{currentStatus}' to '{requestedStatus}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;
    internal WorkOrderStatus CurrentStatus { get; } = currentStatus;
    internal WorkOrderStatus RequestedStatus { get; } = requestedStatus;
}
