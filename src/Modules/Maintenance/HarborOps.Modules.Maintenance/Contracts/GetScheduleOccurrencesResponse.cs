using System;
using System.Collections.Generic;
using System.Text;

namespace HarborOps.Modules.Maintenance.Contracts;

public sealed record GetScheduleOccurrencesResponse(
    Guid ScheduleId,
    string MaintenanceType,
    string Frequency,
    DateOnly? NextDueDate,
    IReadOnlyList<ScheduleOccurrenceListItem> Items);
