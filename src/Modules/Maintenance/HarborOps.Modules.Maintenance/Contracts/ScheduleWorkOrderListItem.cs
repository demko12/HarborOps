using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ScheduleWorkOrderListItem(
    Guid WorkOrderId,
    Guid OccurrenceId,
    Guid VesselId,
    DateOnly OccurrenceDueDate,
    string Title,
    string Status,
    string Priority,
    string OccurrenceState,
    DateTime DueUtc,
    DateTime CreatedUtc,
    DateTime? CompletedUtc);
