using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class WorkOrderEquipmentChangeNotAllowedException(
    Guid workOrderId,
    WorkOrderStatus currentStatus)
    : Exception(
        $"Work order '{workOrderId}' cannot change its equipment scope from status '{currentStatus}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal WorkOrderStatus CurrentStatus { get; } = currentStatus;
}
