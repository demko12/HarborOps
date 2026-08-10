using System;
using System.Collections.Generic;
using System.Text;
using HarborOps.Modules.Maintenance.Domain;

namespace HarborOps.Modules.Maintenance.Application;

internal sealed class CompleteMaintenanceWorkOrderValidationException(
    IReadOnlyDictionary<string, string[]> errors)
    : Exception("Complete maintenance work order validation failed.")
{
    internal IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

internal sealed class MaintenanceWorkOrderNotFoundException(Guid workOrderId)
    : Exception($"Maintenance work order '{workOrderId}' was not found.")
{
    internal Guid WorkOrderId { get; } = workOrderId;
}

internal sealed class CompleteMaintenanceWorkOrderConflictException(Guid workOrderId, WorkOrderStatus currentStatus)
    : Exception($"Work order '{workOrderId}' cannot be completed from status '{currentStatus}'.")
{
    internal Guid WorkOrderId { get; } = workOrderId;

    internal WorkOrderStatus CurrentStatus { get; } = currentStatus;
}
