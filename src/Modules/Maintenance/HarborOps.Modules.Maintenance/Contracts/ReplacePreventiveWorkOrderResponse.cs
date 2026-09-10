using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record ReplacePreventiveWorkOrderResponse(
    Guid OccurrenceId,
    Guid ScheduleId,
    Guid VesselId,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    Guid WorkOrderId,
    Guid? ReplacedWorkOrderId,
    IReadOnlyList<Guid> EquipmentIds,
    DateTime GeneratedUtc);
