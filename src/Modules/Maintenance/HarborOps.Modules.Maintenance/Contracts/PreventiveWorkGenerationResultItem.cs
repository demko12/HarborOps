using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PreventiveWorkGenerationResultItem(
    Guid ScheduleId,
    Guid OccurrenceId,
    Guid VesselId,
    string MaintenanceType,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    string Outcome,
    Guid WorkOrderId,
    IReadOnlyList<Guid> EquipmentIds);
