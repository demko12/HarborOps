using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record PreventiveWorkOccurrenceItem(
    Guid ScheduleId,
    Guid? OccurrenceId,
    Guid VesselId,
    string MaintenanceType,
    string Frequency,
    DateOnly DueDate,
    DateTime DueUtc,
    string State,
    bool IsOverdue,
    Guid? WorkOrderId,
    IReadOnlyList<Guid> EquipmentIds);
