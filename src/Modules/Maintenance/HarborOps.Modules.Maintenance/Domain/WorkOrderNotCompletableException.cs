using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class WorkOrderNotCompletableException(Guid workOrderId, WorkOrderStatus currentStatus)
    : Exception($"Work order '{workOrderId}' cannot be completed from status '{currentStatus}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal WorkOrderStatus CurrentStatus { get; } = currentStatus;
}
