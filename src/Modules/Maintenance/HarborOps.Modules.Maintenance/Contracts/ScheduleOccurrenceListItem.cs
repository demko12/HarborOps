using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ScheduleOccurrenceListItem(
    Guid OccurrenceId,
    Guid ScheduleId,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    Guid WorkOrderId,
    Guid? ReplacedWorkOrderId,
    DateTime GeneratedUtc,
    DateTime? ResolvedUtc);
