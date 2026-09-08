using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class WorkOrderPartReservationInvariantException(string reason)
    : Exception(reason)
{
}

internal sealed class WorkOrderReservationChangeNotAllowedException(
    Guid workOrderId,
    WorkOrderStatus currentStatus)
    : Exception(
        $"Work order '{workOrderId}' cannot change its part reservations from status '{currentStatus}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal WorkOrderStatus CurrentStatus { get; } = currentStatus;
}

internal sealed class WorkOrderReservationReleaseNotAllowedException(
    Guid workOrderId,
    WorkOrderStatus currentStatus)
    : Exception(
        $"Work order '{workOrderId}' cannot release its part reservations from status '{currentStatus}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal WorkOrderStatus CurrentStatus { get; } = currentStatus;
}
