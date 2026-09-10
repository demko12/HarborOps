using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Domain;

internal sealed class ScheduleOccurrenceTransitionNotAllowedException(
    Guid occurrenceId,
    ScheduleOccurrenceState currentState,
    ScheduleOccurrenceState requestedState)
    : Exception(
        $"Schedule occurrence '{occurrenceId}' cannot move from '{currentState}' to '{requestedState}'.")
{
    internal Guid OccurrenceId { get; } = occurrenceId;

    internal ScheduleOccurrenceState CurrentState { get; } = currentState;

    internal ScheduleOccurrenceState RequestedState { get; } = requestedState;
}

internal sealed class ScheduleOccurrenceLinkInvalidException(
    Guid occurrenceId,
    Guid linkedWorkOrderId,
    Guid requestedWorkOrderId)
    : Exception(
        $"Schedule occurrence '{occurrenceId}' is linked to work order '{linkedWorkOrderId}', not '{requestedWorkOrderId}'.")
{
    internal Guid OccurrenceId { get; } = occurrenceId;

    internal Guid LinkedWorkOrderId { get; } = linkedWorkOrderId;

    internal Guid RequestedWorkOrderId { get; } = requestedWorkOrderId;
}
